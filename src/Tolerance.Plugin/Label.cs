using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AutoCAD;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using PvGrade;
using static System.Net.Mime.MediaTypeNames;

namespace LabelTest
{

    public static class ListExtensions
    {
        public static List<List<T>> ChunkBy<T>(this List<T> source, int chunkSize)
        {
            return source
                .Select((x, i) => new { Index = i, Value = x })
                .GroupBy(x => x.Index / chunkSize)
                .Select(x => x.Select(v => v.Value).ToList())
                .ToList();
        }
    }
    class BlockJig : EntityJig
    {
        BlockReference br;
        Polyline pline;
        Point3d dragPt;
        Plane plane;
        Dictionary<AttributeReference, TextInfo> attInfos;

        public BlockJig(BlockReference br, Polyline pline, Dictionary<AttributeReference, TextInfo> attInfos) : base(br)
        {
            this.br = br;
            this.pline = pline;
            this.attInfos = attInfos;
            plane = new Plane(Point3d.Origin, pline.Normal);
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var options = new JigPromptPointOptions("\nSpecicfy insertion point: ");
            options.UserInputControls =
                UserInputControls.Accept3dCoordinates |
                UserInputControls.UseBasePointElevation;
            var result = prompts.AcquirePoint(options);
            if (result.Value.IsEqualTo(dragPt))
                return SamplerStatus.NoChange;
            dragPt = result.Value;
            return SamplerStatus.OK;
        }

        protected override bool Update()
        {
            var point = pline.GetClosestPointTo(dragPt, false);
            var angle = pline.GetFirstDerivative(point).AngleOnPlane(plane);
            br.Position = point;
            br.Rotation = angle;
            foreach (var entry in attInfos)
            {
                var att = entry.Key;
                var info = entry.Value;
                att.Position = info.Position.TransformBy(br.BlockTransform);
                att.Rotation = info.Rotation + angle;
                if (info.IsAligned)
                {
                    att.AlignmentPoint = info.Alignment.TransformBy(br.BlockTransform);
                    att.AdjustAlignment(br.Database);
                }
                if (att.IsMTextAttribute)
                {
                    att.UpdateMTextAttribute();
                }
            }
            return true;
        }
    }


    class TextInfo
    {
        public Point3d Position { get; }

        public Point3d Alignment { get; }

        public bool IsAligned { get; }

        public double Rotation { get; }

        public TextInfo(DBText text)
        {
            Position = text.Position;
            IsAligned = text.Justify != AttachmentPoint.BaseLeft;
            Alignment = text.AlignmentPoint;
            Rotation = text.Rotation;
        }
    }

    class TextJig : EntityJig
    {
        DBText text;
        Polyline pline;
        Point3d dragPt;
        Plane plane;
        Database db;

        public TextJig(DBText text, Polyline pline) : base(text)
        {
            this.text = text;
            this.pline = pline;
            plane = new Plane(Point3d.Origin, pline.Normal);
            db = HostApplicationServices.WorkingDatabase;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var options = new JigPromptPointOptions("\nSpecicfy insertion point: ");
            options.UserInputControls =
                UserInputControls.Accept3dCoordinates |
                UserInputControls.UseBasePointElevation;
            var result = prompts.AcquirePoint(options);
            if (result.Value.IsEqualTo(dragPt))
                return SamplerStatus.NoChange;
            dragPt = result.Value;
            return SamplerStatus.OK;
        }

        protected override bool Update()
        {
            var point = pline.GetClosestPointTo(dragPt, false);
            var angle = pline.GetFirstDerivative(point).AngleOnPlane(plane);
            //if angle greater than 90 or less than 270, add 180
            if (angle > 1.5708 && angle < 4.71239)
            {
                angle = angle + 3.14159;
            }

            text.AlignmentPoint = point;
            text.Rotation = angle;
            text.AdjustAlignment(db);
            return true;
        }
    }
    public class LabelText
    {
        public static void addTextLabel(double x, double y, double elevation, double scaler, BlockTableRecord btr, Transaction transaction)
        {
            using (DBText acText = new DBText())
            {
                acText.Height = scaler * .1;
                acText.Position = new Point3d(x,y,elevation);
                acText.TextString = Math.Round(Convert.ToDecimal(elevation), 2, MidpointRounding.AwayFromZero).ToString();
                //acText.Layer = "E_SPOT";

                btr.AppendEntity(acText);
                transaction.AddNewlyCreatedDBObject(acText, true);
            }           
        }

        public static Point3d GetOffset(Autodesk.AutoCAD.DatabaseServices.DBObject point, double offsetX, double offsetY)
        {
            var pointX = (point.Bounds.Value.MaxPoint.X + point.Bounds.Value.MinPoint.X) / 2;
            var pointY = (point.Bounds.Value.MaxPoint.Y + point.Bounds.Value.MinPoint.Y) / 2;
            return new Point3d(pointX+offsetX,pointY+offsetY, point.Bounds.Value.MinPoint.Z);
        }
        public static void LabelContour()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database docDb = doc.Database;
            var ed = doc.Editor;
            var docScale = docDb.Cannoscale.DrawingUnits;
            using (Transaction transaction = doc.Database.TransactionManager.StartTransaction())
            {


                

                BlockTable acBlkTbl;
                acBlkTbl = transaction.GetObject(docDb.BlockTableId,
                                                OpenMode.ForRead) as BlockTable;
                BlockTableRecord acBlkTblRec;
                acBlkTblRec = transaction.GetObject(acBlkTbl[BlockTableRecord.ModelSpace],
                                                OpenMode.ForWrite) as BlockTableRecord;


                var textHeightOptions = new PromptDistanceOptions("\nEnter Text Height (Default is .1 x Document Scale)");
                textHeightOptions.DefaultValue = docScale*.1;
                textHeightOptions.AllowNegative = false;
                var textHeight = doc.Editor.GetDistance(textHeightOptions);
                if (textHeight.Status != PromptStatus.OK)
                {
                    return;
                }

                var options = new PromptEntityOptions("\nSelect contour to label");
                options.SetRejectMessage("\nSelected object is no a Polyline.");
                options.AddAllowedClass(typeof(Polyline), true);
                var polylineResult = doc.Editor.GetEntity(options);
                if (polylineResult.Status != PromptStatus.OK)
                {
                    return;
                }
                while (polylineResult.Status == PromptStatus.OK)
                {
                    var pline = (Polyline)transaction.GetObject(polylineResult.ObjectId, OpenMode.ForRead);
                    using (var text = new DBText())
                    {
                        text.SetDatabaseDefaults();
                        text.Normal = pline.Normal;
                        text.Justify = AttachmentPoint.MiddleCenter;
                        text.AlignmentPoint = Point3d.Origin;
                        text.TextString = pline.Elevation.ToString();
                        text.Height = textHeight.Value;
                        text.Layer = "VA-SURF-MAJR-LABL";


                        var jig = new TextJig(text, pline);
                        var result = ed.Drag(jig);
                        if (result.Status == PromptStatus.OK)
                        {
                            var currentSpace = (BlockTableRecord)transaction.GetObject(docDb.CurrentSpaceId, OpenMode.ForWrite);
                            currentSpace.AppendEntity(text);
                            transaction.AddNewlyCreatedDBObject(text, true);
                        }
                    }
                    docDb.TransactionManager.QueueForGraphicsFlush();
                    //doc.Editor.Regen();
                    
                    polylineResult = doc.Editor.GetEntity(options);

                }
                transaction.Commit();

                //addTextLabel(x, y, contour.Elevation, docScale, acBlkTblRec, transaction);


            }
        }
            public static void LabelTextZ()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database docDb = doc.Database;
            using (Transaction transaction = doc.Database.TransactionManager.StartTransaction())
            {
                var offsetXPromptOptions = new PromptDistanceOptions("\nOffset X");
                offsetXPromptOptions.DefaultValue = 5.0;
                offsetXPromptOptions.AllowNegative = true;
                var offsetXPromptResult = doc.Editor.GetDistance(offsetXPromptOptions);
                if (offsetXPromptResult.Status != PromptStatus.OK)
                {
                    return;
                }

                var offsetYPromptOptions = new PromptDistanceOptions("\nOffset Y");
                offsetYPromptOptions.DefaultValue = -5.0;
                offsetYPromptOptions.AllowNegative = true;
                var offsetYPromptResult = doc.Editor.GetDistance(offsetYPromptOptions);
                if (offsetYPromptResult.Status != PromptStatus.OK)
                {
                    return;
                }
                var textHeightPromptOptions = new PromptDistanceOptions("\nText Height");
                textHeightPromptOptions.DefaultValue = 3.2;
                textHeightPromptOptions.AllowNegative = true;
                var textHeightPromptResult = doc.Editor.GetDistance(textHeightPromptOptions);
                if (textHeightPromptResult.Status != PromptStatus.OK)
                {
                    return;
                }


                var pointsPromptOptions = new PromptSelectionOptions();
                pointsPromptOptions.MessageForAdding = "\nSelect points to label";
                var pointsSelectionResult = doc.Editor.GetSelection(pointsPromptOptions);
                if (pointsSelectionResult.Status != PromptStatus.OK)
                {
                    return;
                }

                BlockTable acBlkTbl;
                acBlkTbl = transaction.GetObject(docDb.BlockTableId,
                                                OpenMode.ForRead) as BlockTable;

                // Open the Block table record Model space for write
                BlockTableRecord acBlkTblRec;
                acBlkTblRec = transaction.GetObject(acBlkTbl[BlockTableRecord.ModelSpace],
                                                OpenMode.ForWrite) as BlockTableRecord;

                var pointsIds = pointsSelectionResult.Value.GetObjectIds().ToList();
                foreach (var pointsId in pointsIds)
                {
                    var point = transaction.GetObject(pointsId, OpenMode.ForRead);
                    if (point is BlockReference)
                    {

                        using (DBText acText = new DBText())
                        {
                            acText.Position = GetOffset(point, offsetXPromptResult.Value, offsetYPromptResult.Value);
                            acText.Height = textHeightPromptResult.Value;
                            acText.TextString = Math.Round(point.Bounds.Value.MinPoint.Z, 1, MidpointRounding.AwayFromZero).ToString();
                            acText.Layer = "E_SPOT";

                            acBlkTblRec.AppendEntity(acText);
                            transaction.AddNewlyCreatedDBObject(acText, true);
                        }
                    }

                }

                doc.TransactionManager.EnableGraphicsFlush(true);
                doc.TransactionManager.FlushGraphics();
                doc.Editor.Regen();
                transaction.Commit();
            }
        }
    }
}
