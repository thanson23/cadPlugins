using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AutoCAD;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Windows;
using Autodesk.Civil;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using PvGrade;

namespace vmrControl
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

    public class vmrControl
    {
        public static void addTextLabel(string[] line, double scaler, BlockTableRecord btr, Transaction transaction)
        {
            for (int i = 0; i < 4; i++)
            {
                using (DBText acText = new DBText())
                {
                    acText.Height = scaler * .1;
                    acText.Position = new Point3d(double.Parse(line[1]) + .28*scaler, double.Parse(line[2])-(acText.Height/2)-((i-1)*acText.Height *1.25) , double.Parse(line[3]));
                    
                    if (i == 0)
                        acText.TextString = line[0];
                    else
                        acText.TextString = Math.Round(Convert.ToDecimal(line[i]), 2, MidpointRounding.AwayFromZero).ToString();
                    //acText.Layer = "E_SPOT";

                    btr.AppendEntity(acText);
                    transaction.AddNewlyCreatedDBObject(acText, true);
                }
            }
        }


public static Point3d GetOffset(Autodesk.AutoCAD.DatabaseServices.DBObject point, double offsetX, double offsetY)
        {
            var pointX = (point.Bounds.Value.MaxPoint.X + point.Bounds.Value.MinPoint.X) / 2;
            var pointY = (point.Bounds.Value.MaxPoint.Y + point.Bounds.Value.MinPoint.Y) / 2;
            return new Point3d(pointX+offsetX,pointY+offsetY, point.Bounds.Value.MinPoint.Z);
        }

        public static void LabelControl()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            Database docDb = doc.Database;
            var docScale = docDb.Cannoscale.DrawingUnits;
            using (Transaction transaction = doc.Database.TransactionManager.StartTransaction())
            {
                //var scalePromptOptions = new PromptDistanceOptions("\nScaler");
                //scalePromptOptions.DefaultValue = 40;
                //scalePromptOptions.AllowNegative = true;
                //var scalePromptResult = doc.Editor.GetDistance(scalePromptOptions);
                //if (scalePromptResult.Status != PromptStatus.OK)
                //{
                //    return;
                //}
                PromptOpenFileOptions pofo = new PromptOpenFileOptions("\nEnter File");
                pofo.PreferCommandLine = false;
                pofo.DialogName = "Select File";
                pofo.DialogCaption = "Select File";

                PromptFileNameResult pfnr = Application.DocumentManager.MdiActiveDocument.Editor.GetFileNameForOpen(pofo);
                if (pfnr.Status != PromptStatus.OK) return;

                string FileName = pfnr.StringResult;
                FileStream Fstream = new FileStream(FileName, FileMode.Open);
                StreamReader read = new StreamReader(Fstream);
                string st = read.ReadLine();

                
                using (Database OpenDb = new Database(false, true))
                {
                    OpenDb.ReadDwgFile("Y:/projects/labelTesting/blocks.dwg", System.IO.FileShare.ReadWrite, true, "");
                    var blkname = "Aerial Ctrl";
                    ObjectIdCollection ids = new ObjectIdCollection();

                    using (Transaction tr = OpenDb.TransactionManager.StartTransaction())
                    {
                        //For example, Get the block by name "BlkName"
                        BlockTable bt;
                        bt = (BlockTable)tr.GetObject(OpenDb.BlockTableId, OpenMode.ForRead);

                        if (bt.Has(blkname))
                        {
                            ids.Add(bt[blkname]);
                        }
                        tr.Commit();
                    }
                    //if found, add the block
                    if (ids.Count != 0)
                    {
                        //get the current drawing database
                        Database destdb = doc.Database;

                        IdMapping iMap = new IdMapping();
                        destdb.WblockCloneObjects(ids, destdb.BlockTableId, iMap, DuplicateRecordCloning.Ignore, false);

                    }
                }

                BlockTable acBlkTbl;
                acBlkTbl = transaction.GetObject(docDb.BlockTableId,
                                                OpenMode.ForRead) as BlockTable;

                BlockTableRecord blockDef = acBlkTbl["Aerial Ctrl"].GetObject(OpenMode.ForRead) as BlockTableRecord;

                // Open the Block table record Model space for write
                BlockTableRecord acBlkTblRec;
                acBlkTblRec = transaction.GetObject(acBlkTbl[BlockTableRecord.ModelSpace],
                                                OpenMode.ForWrite) as BlockTableRecord;

                
                while (st != null)
                {
                    var line = st.Split(',');
                    var test = new BlockReference(new Point3d(double.Parse(line[1]), double.Parse(line[2]), double.Parse(line[3])), blockDef.ObjectId);
                    test.ScaleFactors = new Scale3d(docScale);
                    test.BlockUnit = UnitsValue.Undefined;
                    acBlkTblRec.AppendEntity(test);
                    transaction.AddNewlyCreatedDBObject(test, true);

                    addTextLabel(line, docScale, acBlkTblRec, transaction);
                    //using (MText acMText = new MText())
                    //{
                    //    acMText.Location = new Point3d(double.Parse(line[1])+3, double.Parse(line[2]), double.Parse(line[3]));
                    //    acMText.Contents = line[0] + "\n" + Math.Round(Convert.ToDecimal(line[1])) + "\n" + Math.Round(Convert.ToDecimal(line[2])) + "\n" + Math.Round(Convert.ToDecimal(line[3]));
                    //    acMText.TextHeight = scalePromptResult.Value * .025;
                    //    acBlkTblRec.AppendEntity(acMText);
                    //    transaction.AddNewlyCreatedDBObject(acMText, true);
                    //}
                    //doc.Editor.WriteMessage("\n " + line[0] + " " + line[1]);
                    st = read.ReadLine();
                }
                Fstream.Dispose();


                doc.TransactionManager.EnableGraphicsFlush(true);
                doc.TransactionManager.FlushGraphics();
                doc.Editor.Regen();
                transaction.Commit();
            }
        }
    }
}
