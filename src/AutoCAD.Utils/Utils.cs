using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.BoundaryRepresentation;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;

namespace AutoCAD
{
    public static class Utils
    {
        public enum PositionOnRectangle
        {
            Inside,
            Left,
            Right,
            Top,
            Bottom,
            Outside
        }

        public static bool EqualWithinTolerance(double a, double b, double tolerance)
        {
            return Math.Abs(a - b) <= tolerance;
        }

        public static bool LayerExists(string layerName, Transaction transaction)
        {
            LayerTable layerTable = transaction.GetObject(Application.DocumentManager.MdiActiveDocument.Database.LayerTableId, OpenMode.ForRead) as LayerTable;
            return layerTable.Has(layerName);
        }

        public static ObjectId GetOrCreateLayer(string layerName, Transaction transaction)
        {
            LayerTable layerTable = transaction.GetObject(Application.DocumentManager.MdiActiveDocument.Database.LayerTableId, OpenMode.ForRead) as LayerTable;
            if (!layerTable.Has(layerName))
            {
                layerTable.UpgradeOpen();
                LayerTableRecord newLayer = new LayerTableRecord();
                newLayer.Name = layerName;
                layerTable.Add(newLayer);
                transaction.AddNewlyCreatedDBObject(newLayer, true);
            }
            return layerTable[layerName];
        }

        public static ObjectId GetOrCreateSurfaceStyle(string styleName, Transaction transaction)
        {
            foreach (var styleId in CivilApplication.ActiveDocument.Styles.SurfaceStyles)
            {
                var style = transaction.GetObject(styleId, OpenMode.ForRead) as SurfaceStyle;
                if (style.Name == styleName)
                {
                    return style.ObjectId;
                }
            }
            return CivilApplication.ActiveDocument.Styles.SurfaceStyles.Add(styleName);
        }

        public static ObjectId GetOrCreatePointGroup(string groupName)
        {
            var pointGroup = CivilApplication
                .ActiveDocument
                .PointGroups
                .Select(x => x.GetObject(OpenMode.ForRead) as PointGroup)
                .FirstOrDefault(x => x.Name == groupName);
            if (pointGroup == null)
            {
                return CivilApplication.ActiveDocument.PointGroups.Add(groupName);
            }
            return pointGroup.ObjectId;
        }

        public static Polyline ToLightweightPolyline(Autodesk.AutoCAD.DatabaseServices.DBObject inputPolyline, Transaction transaction)
        {     
            var newPolyline = new Polyline();
            if (inputPolyline is Polyline)
            {
                var p = inputPolyline as Polyline;
                for (var i = 0; i < p.NumberOfVertices; i++)
                {
                    newPolyline.AddVertexAt(i, p.GetPoint2dAt(i), 0, 0, 0);
                }
            }
            else if (inputPolyline is Polyline2d)
            {
                var p = inputPolyline as Polyline2d;
                var i = 0;
                foreach (ObjectId vertexId in p)
                {
                    var vertex = transaction.GetObject(vertexId, OpenMode.ForRead) as Vertex2d;
                    newPolyline.AddVertexAt(i++, new Point2d(vertex.Position.X, vertex.Position.Y), 0, 0, 0);
                }
            }
            else if (inputPolyline is Polyline3d)
            {
                var p = inputPolyline as Polyline3d;
                var i = 0;
                foreach (ObjectId vertexId in p)
                {
                    var vertex = transaction.GetObject(vertexId, OpenMode.ForRead) as PolylineVertex3d;
                    newPolyline.AddVertexAt(i++, new Point2d(vertex.Position.X, vertex.Position.Y), 0, 0, 0);
                }
            }
            return newPolyline;
        }

        public static bool IsRectangle(ObjectId objectId, Transaction transaction)
        {
            if (objectId.ObjectClass != RXClass.GetClass(typeof(Polyline)))
            {
                return false;
            }
            var polyline = transaction.GetObject(objectId, OpenMode.ForRead) as Polyline;
            if (!polyline.IsOnlyLines || polyline.NumberOfVertices != 4)
            {
                return false;
            }
            for (int i = 0; i < polyline.NumberOfVertices - 2; i++)
            {
                var a = polyline.GetLineSegment2dAt(i);
                var b = polyline.GetLineSegment2dAt(i + 1);
                if (!a.IsPerpendicularTo(b))
                {
                    return false;
                }
            }
            return true;
        }

        public static bool IsOnRectangle(this PositionOnRectangle position)
        {
            return position != PositionOnRectangle.Inside && position != PositionOnRectangle.Outside;
        }

        public static bool IsPointOnRectangle(Polyline rectangle, Point3d point, Transaction transaction)
        {
            var position = GetPositionOnRectangle(rectangle, point, transaction);
            return IsOnRectangle(position);
        }

        public static PositionOnRectangle GetPositionOnRectangle(Polyline rectangle, Point3d point, Transaction transaction)
        {
            var x1 = rectangle.Bounds.Value.MinPoint.X;
            var x2 = rectangle.Bounds.Value.MaxPoint.X;
            var y1 = rectangle.Bounds.Value.MinPoint.Y;
            var y2 = rectangle.Bounds.Value.MaxPoint.Y;
            var tolerance = 0.01;
            if (point.Y >= y1 && point.Y <= y2 && EqualWithinTolerance(point.X, x1, tolerance))
            {
                return PositionOnRectangle.Left;
            }
            else if (point.Y >= y1 && point.Y <= y2 && EqualWithinTolerance(point.X, x2, tolerance))
            {
                return PositionOnRectangle.Right;
            }
            else if (point.X >= x1 && point.X <= x2 && EqualWithinTolerance(point.Y, y1, tolerance))
            {
                return PositionOnRectangle.Bottom;
            }
            else if (point.X >= x1 && point.X <= x2 && EqualWithinTolerance(point.Y, y2, tolerance))
            {
                return PositionOnRectangle.Top;
            }
            else if (point.X > x1 && point.X < x2 && point.Y > y1 && point.Y < y2)
            {
                return PositionOnRectangle.Inside;
            }
            else
            {
                return PositionOnRectangle.Outside;
            }
        }

        public static List<List<Point3d>> CreatePointGrid(double trackerSpacing, double trackerWidth, ObjectId boundingRectangleId, ObjectId inputSurfaceId, Transaction transaction)
        {
            var inputSurface = transaction.GetObject(inputSurfaceId, OpenMode.ForRead) as Autodesk.Civil.DatabaseServices.Surface;
            var boundingRectangle = transaction.GetObject(boundingRectangleId, OpenMode.ForRead) as Polyline;
            var minPoint = boundingRectangle.Bounds.Value.MinPoint;
            var maxPoint = boundingRectangle.Bounds.Value.MaxPoint;
            var lowerLeft = new Point3d(minPoint.X + trackerWidth / 2, minPoint.Y, minPoint.Z);
            var upperRight = new Point3d(maxPoint.X - trackerWidth / 2, maxPoint.Y, maxPoint.Z);

            var width = upperRight.X - lowerLeft.X;
            var height = upperRight.Y - lowerLeft.Y;
            var xSpacing = trackerSpacing;
            var numberOfXPoints = (int)(width / xSpacing) + 1;
            var numberOfYPoints = 21;
            var ySpacing = height / (numberOfYPoints - 1);

            var pointGrid = new List<List<Point3d>>();

            for (var i = 0; i < numberOfXPoints; i++)
            {
                var column = new List<Point3d>();
                for (var j = 0; j < numberOfYPoints; j++)
                {
                    var x = lowerLeft.X + i * xSpacing;
                    var y = lowerLeft.Y + j * ySpacing;
                    var z = inputSurface.FindElevationAtXY(x, y);
                    column.Add(new Point3d(x, y, z));
                }
                pointGrid.Add(column);
            }

            return pointGrid;
        }

        public static List<List<Point3d>> CreateNByNPointGrid(int n, ObjectId boundingRectangleId, ObjectId inputSurfaceId, Transaction transaction)
        {
            var inputSurface = transaction.GetObject(inputSurfaceId, OpenMode.ForRead) as Autodesk.Civil.DatabaseServices.Surface;
            var boundingRectangle = transaction.GetObject(boundingRectangleId, OpenMode.ForRead) as Polyline;
            var lowerLeft = boundingRectangle.Bounds.Value.MinPoint;
            var upperRight = boundingRectangle.Bounds.Value.MaxPoint;

            var width = upperRight.X - lowerLeft.X;
            var height = upperRight.Y - lowerLeft.Y;
            var xSpacing = width / (n - 1);
            var ySpacing = height / (n - 1);

            var pointGrid = new List<List<Point3d>>();

            for (var i = 0; i < n; i++)
            {
                var column = new List<Point3d>();
                for (var j = 0; j < n; j++)
                {
                    var x = lowerLeft.X + j * xSpacing;
                    var y = lowerLeft.Y + i * ySpacing;
                    var z = inputSurface.FindElevationAtXY(x, y);
                    column.Add(new Point3d(x, y, z));
                }
                pointGrid.Add(column);
            }

            // Reverse the order of the rows so that points with greater Y values are at the top
            pointGrid.Reverse();

            return pointGrid;
        }

        public static bool IsInside(Polyline a, Polyline b, Transaction transaction)
        {
            var x1 = a.Bounds.Value.MinPoint.X;
            var x2 = a.Bounds.Value.MaxPoint.X;
            var y1 = a.Bounds.Value.MinPoint.Y;
            var y2 = a.Bounds.Value.MaxPoint.Y;
            if (!a.Closed)
            {
                return false;
            }
            // Check for trivial rejection with bounding box because it is much more efficient
            for (int i = 0; i < b.NumberOfVertices; i++)
            {
                var p = b.GetPoint2dAt(i);
                if (p.X < x1 || p.X > x2 || p.Y < y1 || p.Y > y2)
                {
                    return false;
                }
            }
            var curves = new DBObjectCollection();
            curves.Add(a);
            try
            {
                var regions = Region.CreateFromCurves(curves).Cast<Region>().ToList();
                var region = regions.First();
                var brep = new Brep(region);
                for (int i = 0; i < b.NumberOfVertices; i++)
                {
                    PointContainment result;
                    using var ent = brep.GetPointContainment(b.GetPoint3dAt(i), out result);
                    if (result == PointContainment.Outside)
                    {
                        regions.ForEach(r => r.Dispose());
                        return false;
                    }
                }
                regions.ForEach(r => r.Dispose());
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        public static bool IsInside(Polyline boundary, Point3d point)
        {
            var x1 = boundary.Bounds.Value.MinPoint.X;
            var x2 = boundary.Bounds.Value.MaxPoint.X;
            var y1 = boundary.Bounds.Value.MinPoint.Y;
            var y2 = boundary.Bounds.Value.MaxPoint.Y;
            if (!boundary.Closed)
            {
                return false;
            }
            // Check for trivial rejection with bounding box because it is much more efficient
            if (point.X < x1 || point.X > x2 || point.Y < y1 || point.Y > y2)
            {
                return false;
            }
            var curves = new DBObjectCollection();
            curves.Add(boundary);
            try
            {
                var regions = Region.CreateFromCurves(curves).Cast<Region>().ToList();
                var region = regions.First();
                var brep = new Brep(region);
                PointContainment result;
                using var ent = brep.GetPointContainment(point, out result);
                regions.ForEach(r => r.Dispose());
                return result != PointContainment.Outside;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        public static Polyline SnapToBoundary(Polyline boundary, Polyline polyline, Transaction transaction)
        {
            var x1 = boundary.Bounds.Value.MinPoint.X;
            var x2 = boundary.Bounds.Value.MaxPoint.X;
            var y1 = boundary.Bounds.Value.MinPoint.Y;
            var y2 = boundary.Bounds.Value.MaxPoint.Y;
            var snapTolerance = 20;
            var minimumOpenDistance = 5;
            var newPolyline = new Polyline();
            var points = new List<Point2d>();
            for (int i = 0; i < polyline.NumberOfVertices; i++)
            {
                points.Add(polyline.GetPoint2dAt(i));
            }

            if (!polyline.Closed && polyline.StartPoint.DistanceTo(polyline.EndPoint) >= minimumOpenDistance)
            {
                if (EqualWithinTolerance(points[0].X, x1, snapTolerance))
                {
                    points[0] = new Point2d(x1, points[0].Y);
                }
                if (EqualWithinTolerance(x2, points[0].X, snapTolerance))
                {
                    points[0] = new Point2d(x2, points[0].Y);
                }
                if (EqualWithinTolerance(points[0].Y, y1, snapTolerance))
                {
                    points[0] = new Point2d(points[0].X, y1);
                }
                if (EqualWithinTolerance(y2, points[0].Y, snapTolerance))
                {
                    points[0] = new Point2d(points[0].X, y2);
                }
                if (EqualWithinTolerance(points[points.Count - 1].X, x1, snapTolerance))
                {
                    points[points.Count - 1] = new Point2d(x1, points[points.Count - 1].Y);
                }
                if (EqualWithinTolerance(x2, points[points.Count - 1].X, snapTolerance))
                {
                    points[points.Count - 1] = new Point2d(x2, points[points.Count - 1].Y);
                }
                if (EqualWithinTolerance(points[points.Count - 1].Y, y1, snapTolerance))
                {
                    points[points.Count - 1] = new Point2d(points[points.Count - 1].X, y1);
                }
                if (EqualWithinTolerance(y2, points[points.Count - 1].Y, snapTolerance))
                {
                    points[points.Count - 1] = new Point2d(points[points.Count - 1].X, y2);
                }
            }

            for (int i = 0; i < points.Count; i++)
            {
                newPolyline.AddVertexAt(i, points[i], 0, 0, 0);
            }
            return newPolyline;
        }

        public static Polyline WeedVertices(Polyline boundingRectangle, Polyline polyline, Transaction transaction)
        {
            var newPolyline = new Polyline();
            var i = 0;
            var a = polyline.GetPoint2dAt(i);
            while (i < polyline.NumberOfVertices - 1)
            {
                var b = polyline.GetPoint2dAt(++i);
                if (a.GetDistanceTo(b) > 5 || IsPointOnRectangle(boundingRectangle, new Point3d(b.X, b.Y, 0), transaction))
                {
                    newPolyline.AddVertexAt(newPolyline.NumberOfVertices, a, 0, 0, 0);
                    a = b;
                }
            }
            newPolyline.Closed = polyline.Closed;
            return newPolyline;
        }

        public static Nullable<Point3d> GetIntersection(Point2d p1, Point2d p2, Point2d p3, Point2d p4)
        {
            var t = ((p1.X - p3.X) * (p3.Y - p4.Y) - (p1.Y - p3.Y) * (p3.X - p4.X)) / ((p1.X - p2.X) * (p3.Y - p4.Y) - (p1.Y - p2.Y) * (p3.X - p4.X));
            var u = ((p1.X - p3.X) * (p1.Y - p2.Y) - (p1.Y - p3.Y) * (p1.X - p2.X)) / ((p1.X - p2.X) * (p3.Y - p4.Y) - (p1.Y - p2.Y) * (p3.X - p4.X));

            if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
            {
                return new Point3d(p1.X + t * (p2.X - p1.X), p1.Y + t * (p2.Y - p1.Y), 0);
            }
            return null;
        }

        public static List<Point3d> GetIntersections(Autodesk.AutoCAD.DatabaseServices.DBObject polylineA, Autodesk.AutoCAD.DatabaseServices.DBObject polylineB, Transaction transaction)
        {
            var aPoints = new List<Point2d>();
            if (polylineA as Polyline != null)
            {
                var p = polylineA as Polyline;
                for (var i = 0; i < p.NumberOfVertices; i++)
                {
                    aPoints.Add(p.GetPoint2dAt(i));
                }
                if (p.Closed && aPoints.Last() != aPoints.First())
                {
                    aPoints.Add(aPoints.First());
                }
            }
            else if (polylineA as Polyline2d != null)
            {
                var p = polylineA as Polyline2d;
                foreach (ObjectId vertexId in p)
                {
                    var vertex = transaction.GetObject(vertexId, OpenMode.ForRead) as Vertex2d;
                    aPoints.Add(new Point2d(vertex.Position.X, vertex.Position.Y));
                }
                if (p.Closed && aPoints.Last() != aPoints.First())
                {
                    aPoints.Add(aPoints.First());
                }
            }
            else if (polylineA as Polyline3d != null)
            {
                var p = polylineA as Polyline3d;
                foreach (ObjectId vertexId in p)
                {
                    var vertex = transaction.GetObject(vertexId, OpenMode.ForRead) as PolylineVertex3d;
                    aPoints.Add(new Point2d(vertex.Position.X, vertex.Position.Y));
                }
                if (p.Closed && aPoints.Last() != aPoints.First())
                {
                    aPoints.Add(aPoints.First());
                }
            }

            var bPoints = new List<Point2d>();
            if (polylineB as Polyline != null)
            {
                var p = polylineB as Polyline;
                for (var i = 0; i < p.NumberOfVertices; i++)
                {
                    bPoints.Add(p.GetPoint2dAt(i));
                }
                if (p.Closed && bPoints.Last() != bPoints.First())
                {
                    bPoints.Add(bPoints.First());
                }
            }
            else if (polylineB as Polyline2d != null)
            {
                var p = polylineB as Polyline2d;
                foreach (ObjectId vertexId in p)
                {
                    var vertex = transaction.GetObject(vertexId, OpenMode.ForRead) as Vertex2d;
                    bPoints.Add(new Point2d(vertex.Position.X, vertex.Position.Y));
                }
                if (p.Closed && bPoints.Last() != bPoints.First())
                {
                    bPoints.Add(bPoints.First());
                }
            }
            else if (polylineB as Polyline3d != null)
            {
                var p = polylineB as Polyline3d;
                foreach (ObjectId vertexId in p)
                {
                    var vertex = transaction.GetObject(vertexId, OpenMode.ForRead) as PolylineVertex3d;
                    bPoints.Add(new Point2d(vertex.Position.X, vertex.Position.Y));
                }
                if (p.Closed && bPoints.Last() != bPoints.First())
                {
                    bPoints.Add(bPoints.First());
                }
            }

            var intersectionPoints = new List<Point3d>();

            for (int i = 0; i < aPoints.Count - 1; i++)
            {
                var p1 = aPoints[i];
                var p2 = aPoints[i + 1];
                for (int j = 0; j < bPoints.Count - 1; j++)
                {
                    var p3 = bPoints[j];
                    var p4 = bPoints[j + 1];

                    var intersectionPoint = GetIntersection(p1, p2, p3, p4);
                    if (intersectionPoint.HasValue)
                    {
                        intersectionPoints.Add(intersectionPoint.Value);
                    }
                }
            }
            return intersectionPoints.Distinct().ToList();
        }

        public static List<ObjectId> GetIntersectionOfSurfaces(ObjectId surfaceIdA, ObjectId surfaceIdB, Transaction transaction)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var previousCurrentLayerId = doc.Database.Clayer;
            var tempLayerName = "TemporaryLayer";
            var tempLayerId = GetOrCreateLayer(tempLayerName, transaction);
            var tempLayer = transaction.GetObject(tempLayerId, OpenMode.ForWrite) as LayerTableRecord;

            doc.Database.Clayer = tempLayerId;

            doc.Editor.Command("MINIMUMDISTBETWEENSURFACES", surfaceIdA, surfaceIdB, "No", "Yes");

            var boundaryIds = doc.Editor.SelectAll(
                new SelectionFilter(
                    new TypedValue[]
                    {
                        new TypedValue((int)DxfCode.LayerName, tempLayerName)
                    }
                )
            ).Value.GetObjectIds().ToList();

            doc.Database.Clayer = previousCurrentLayerId;
            tempLayer.Erase();
            return boundaryIds;
        }

        public static ObjectId GetTinSurface(string name, Transaction transaction)
        {
            var surfaceIds = CivilApplication.ActiveDocument.GetSurfaceIds();
            for (int i = 0; i < surfaceIds.Count; i++)
            {
                var surface = transaction.GetObject(surfaceIds[i], OpenMode.ForRead);
                if (surface.GetType() == typeof(TinSurface) && ((TinSurface)surface).Name == name)
                {
                    return surface.ObjectId;
                }
            }
            return ObjectId.Null;
        }

        public static ObjectId GetOrCreateTinSurface(string name, ObjectId surfaceStyleId, Transaction transaction)
        {
            ObjectId surfaceId = GetTinSurface(name, transaction);
            if (surfaceId.IsNull)
            {
                surfaceId = TinSurface.Create(name, surfaceStyleId);
            }
            return surfaceId;
        }
    }
}
