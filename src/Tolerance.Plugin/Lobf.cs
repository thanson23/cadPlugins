using System;
using System.Collections;
using System.Collections.Generic;
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

namespace PvGrade
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
    public class LobfOptions
    {
        public double Tolerance { get; set; } = 0.5;
        public double TrackerSpacing { get; set; } = 20.65;
        public double TrackerWidth { get; set; } = 7.43;
        public double MaximumVerticalDistanceBetweenAdjacentRows { get; set; } = 5.5;
        public double MinimumBoundaryArea { get; set; } = 150;
    }

    public class Lobf
    {
        public static List<Point3d> BestFit(List<Point3d> points)
        {
            var nAvg = points.Average(p => p.Y);
            var zAvg = points.Average(p => p.Z);
            var rise = points.Sum(p => (p.Y - nAvg) * (p.Z - zAvg));
            var run = points.Sum(p => Math.Pow(p.Y - nAvg, 2));
            var slope = rise / run;
            var interceptAvg = points.Average(p => p.Z - slope * p.Y);
            return points.Select(p => new Point3d(p.X, p.Y, p.Y * slope + interceptAvg)).ToList();
        }

        public static List<Point3d> ExtendPoints(List<Point3d> points)
        {
            var minY = points.Min(p => p.Y);
            var maxY = points.Max(p => p.Y);
            var newMinY = minY - (maxY - minY);
            var yDelta = Math.Abs(points[0].Y - points[1].Y);
            var zDelta = points[1].Z - points[0].Z;
            var newMinZ = points[0].Z - zDelta * points.Count;
            var extendedPoints = new List<Point3d>();
            for (int i = 0; i < points.Count * 3; i++)
            {
                extendedPoints.Add(new Point3d(points[0].X, newMinY + i * yDelta, newMinZ + i * zDelta));
            }
            if (!AutoCAD.Utils.EqualWithinTolerance(extendedPoints[points.Count].Z, points[0].Z, 0.05))
            {
                throw new Exception("Extended points are not correct");
            }
            return extendedPoints;
        }

        public static List<Point3d> PG(List<Point3d> points, List<Point3d> bestFitPoints)
        {
            return bestFitPoints.Select((bf, i) =>
            {
                if (i == 0 || i == bestFitPoints.Count - 1)
                {
                    return bf;
                }
                else if (points[i].Z > bf.Z + 0.5)
                {
                    return new Point3d(bf.X, bf.Y, bf.Z + 0.5);
                }
                else if (points[i].Z < bf.Z - 0.5)
                {
                    return new Point3d(bf.X, bf.Y, bf.Z - 0.5);
                }
                else
                {
                    return points[i];
                }
            }).ToList();
        }

        public static Polyline TrimExterior(Polyline boundingRectangle, Polyline polyline, Transaction transaction)
        {
            var startPosition = AutoCAD.Utils.GetPositionOnRectangle(boundingRectangle, polyline.StartPoint, transaction);
            var endPosition = AutoCAD.Utils.GetPositionOnRectangle(boundingRectangle, polyline.EndPoint, transaction);
            var intersectionPoints = AutoCAD.Utils.GetIntersections(boundingRectangle, polyline, transaction);
            var newPolyline = new Polyline();
            var oldPoints = new List<Point3d>();
            for (int i = 0; i < polyline.NumberOfVertices; i++)
            {
                oldPoints.Add(polyline.GetPoint3dAt(i));
            }
            List<Point3d> newPoints;
            if (!polyline.Closed && intersectionPoints.Count > 0 && (startPosition == AutoCAD.Utils.PositionOnRectangle.Outside || endPosition == AutoCAD.Utils.PositionOnRectangle.Outside))
            {
                var startIntersectionPoint = intersectionPoints.OrderBy(polyline.StartPoint.DistanceTo).First();
                var endIntersectionPoint = intersectionPoints.OrderBy(polyline.EndPoint.DistanceTo).First();
                newPoints =
                    oldPoints
                        .SkipWhile(p => AutoCAD.Utils.GetPositionOnRectangle(boundingRectangle, p, transaction) == AutoCAD.Utils.PositionOnRectangle.Outside)
                        .Reverse()
                        .ToList();
                if (!newPoints.Contains(startIntersectionPoint))
                {
                    newPoints.Add(startIntersectionPoint);
                }
                newPoints =
                    newPoints
                        .SkipWhile(p => AutoCAD.Utils.GetPositionOnRectangle(boundingRectangle, p, transaction) == AutoCAD.Utils.PositionOnRectangle.Outside)
                        .Reverse()
                        .ToList();
                if (!newPoints.Contains(endIntersectionPoint))
                {
                    newPoints.Add(endIntersectionPoint);
                }
            }
            else
            {
                newPoints = oldPoints;
            }
            for (int i = 0; i < newPoints.Count; i++)
            {
                newPolyline.AddVertexAt(i, new Point2d(newPoints[i].X, newPoints[i].Y), 0, 0, 0);
            }
            return newPolyline;
        }

        public static Polyline CloseRegion(Polyline boundingRectangle, Polyline polyline, Transaction transaction)
        {
            var startPosition = AutoCAD.Utils.GetPositionOnRectangle(boundingRectangle, polyline.StartPoint, transaction);
            var endPosition = AutoCAD.Utils.GetPositionOnRectangle(boundingRectangle, polyline.EndPoint, transaction);
            var newPolyline = new Polyline();
            var newPoints = new List<Point2d>();
            for (int i = 0; i < polyline.NumberOfVertices; i++)
            {
                newPoints.Add(polyline.GetPoint2dAt(i));
            }
            if (AutoCAD.Utils.IsOnRectangle(startPosition) && AutoCAD.Utils.IsOnRectangle(endPosition))
            {
                var x1 = boundingRectangle.Bounds.Value.MinPoint.X;
                var x2 = boundingRectangle.Bounds.Value.MaxPoint.X;
                var y1 = boundingRectangle.Bounds.Value.MinPoint.Y;
                var y2 = boundingRectangle.Bounds.Value.MaxPoint.Y;
                if (startPosition == AutoCAD.Utils.PositionOnRectangle.Left && endPosition == AutoCAD.Utils.PositionOnRectangle.Left)
                {
                    newPoints.Add(new Point2d(polyline.Bounds.Value.MinPoint.X - 1, newPoints.Last().Y));
                    newPoints.Add(new Point2d(polyline.Bounds.Value.MinPoint.X - 1, newPoints.First().Y));
                    newPoints.Add(newPoints.First());
                }
                if (startPosition == AutoCAD.Utils.PositionOnRectangle.Right && endPosition == AutoCAD.Utils.PositionOnRectangle.Right)
                {
                    newPoints.Add(new Point2d(polyline.Bounds.Value.MaxPoint.X + 1, newPoints.Last().Y));
                    newPoints.Add(new Point2d(polyline.Bounds.Value.MaxPoint.X + 1, newPoints.First().Y));
                    newPoints.Add(newPoints.First());
                }
                if (startPosition == AutoCAD.Utils.PositionOnRectangle.Top && endPosition == AutoCAD.Utils.PositionOnRectangle.Top)
                {
                    newPoints.Add(new Point2d(newPoints.Last().X, polyline.Bounds.Value.MaxPoint.Y + 1));
                    newPoints.Add(new Point2d(newPoints.First().X, polyline.Bounds.Value.MaxPoint.Y + 1));
                    newPoints.Add(newPoints.First());
                }
                if (startPosition == AutoCAD.Utils.PositionOnRectangle.Bottom && endPosition == AutoCAD.Utils.PositionOnRectangle.Bottom)
                {
                    newPoints.Add(new Point2d(newPoints.Last().X, polyline.Bounds.Value.MinPoint.Y - 1));
                    newPoints.Add(new Point2d(newPoints.First().X, polyline.Bounds.Value.MinPoint.Y - 1));
                    newPoints.Add(newPoints.First());
                }
                else if (startPosition == AutoCAD.Utils.PositionOnRectangle.Left && endPosition == AutoCAD.Utils.PositionOnRectangle.Top)
                {
                    newPoints.Add(new Point2d(newPoints.Last().X, polyline.Bounds.Value.MaxPoint.Y + 1));
                    newPoints.Add(new Point2d(polyline.Bounds.Value.MinPoint.X - 1, polyline.Bounds.Value.MaxPoint.Y + 1));
                    newPoints.Add(new Point2d(polyline.Bounds.Value.MinPoint.X - 1, newPoints.First().Y));
                    newPoints.Add(newPoints.First());
                }
                else if (endPosition == AutoCAD.Utils.PositionOnRectangle.Left && startPosition == AutoCAD.Utils.PositionOnRectangle.Top)
                {
                    newPoints.Add(new Point2d(polyline.Bounds.Value.MinPoint.X - 1, newPoints.Last().Y));
                    newPoints.Add(new Point2d(polyline.Bounds.Value.MinPoint.X - 1, polyline.Bounds.Value.MaxPoint.Y + 1));
                    newPoints.Add(new Point2d(newPoints.First().X, polyline.Bounds.Value.MaxPoint.Y + 1));
                    newPoints.Add(newPoints.First());
                }
                else if (startPosition == AutoCAD.Utils.PositionOnRectangle.Right && endPosition == AutoCAD.Utils.PositionOnRectangle.Top)
                {
                    newPoints.Add(new Point2d(newPoints.Last().X, polyline.Bounds.Value.MaxPoint.Y + 1));
                    newPoints.Add(new Point2d(polyline.Bounds.Value.MaxPoint.X + 1, polyline.Bounds.Value.MaxPoint.Y + 1));
                    newPoints.Add(new Point2d(polyline.Bounds.Value.MaxPoint.X + 1, newPoints.First().Y));
                    newPoints.Add(newPoints.First());
                }
                else if (endPosition == AutoCAD.Utils.PositionOnRectangle.Right && startPosition == AutoCAD.Utils.PositionOnRectangle.Top)
                {
                    newPoints.Add(new Point2d(polyline.Bounds.Value.MaxPoint.X + 1, newPoints.Last().Y));
                    newPoints.Add(new Point2d(polyline.Bounds.Value.MaxPoint.X + 1, polyline.Bounds.Value.MaxPoint.Y + 1));
                    newPoints.Add(new Point2d(newPoints.First().X, polyline.Bounds.Value.MaxPoint.Y + 1));
                    newPoints.Add(newPoints.First());
                }
                else if (startPosition == AutoCAD.Utils.PositionOnRectangle.Left && endPosition == AutoCAD.Utils.PositionOnRectangle.Bottom)
                {
                    newPoints.Add(new Point2d(newPoints.Last().X, polyline.Bounds.Value.MinPoint.Y - 1));
                    newPoints.Add(new Point2d(polyline.Bounds.Value.MinPoint.X - 1, polyline.Bounds.Value.MinPoint.Y - 1));
                    newPoints.Add(new Point2d(polyline.Bounds.Value.MinPoint.X - 1, newPoints.First().Y));
                    newPoints.Add(newPoints.First());
                }
                else if (endPosition == AutoCAD.Utils.PositionOnRectangle.Left && startPosition == AutoCAD.Utils.PositionOnRectangle.Bottom)
                {
                    newPoints.Add(new Point2d(polyline.Bounds.Value.MinPoint.X - 1, newPoints.Last().Y));
                    newPoints.Add(new Point2d(polyline.Bounds.Value.MinPoint.X - 1, polyline.Bounds.Value.MinPoint.Y - 1));
                    newPoints.Add(new Point2d(newPoints.First().X, polyline.Bounds.Value.MinPoint.Y - 1));
                    newPoints.Add(newPoints.First());
                }
                else if (startPosition == AutoCAD.Utils.PositionOnRectangle.Right && endPosition == AutoCAD.Utils.PositionOnRectangle.Bottom)
                {
                    newPoints.Add(new Point2d(newPoints.Last().X, polyline.Bounds.Value.MinPoint.Y - 1));
                    newPoints.Add(new Point2d(polyline.Bounds.Value.MaxPoint.X + 1, polyline.Bounds.Value.MinPoint.Y - 1));
                    newPoints.Add(new Point2d(polyline.Bounds.Value.MaxPoint.X + 1, newPoints.First().Y));
                    newPoints.Add(newPoints.First());
                }
                else if (endPosition == AutoCAD.Utils.PositionOnRectangle.Right && startPosition == AutoCAD.Utils.PositionOnRectangle.Bottom)
                {
                    newPoints.Add(new Point2d(polyline.Bounds.Value.MaxPoint.X + 1, newPoints.Last().Y));
                    newPoints.Add(new Point2d(polyline.Bounds.Value.MaxPoint.X + 1, polyline.Bounds.Value.MinPoint.Y - 1));
                    newPoints.Add(new Point2d(newPoints.First().X, polyline.Bounds.Value.MinPoint.Y - 1));
                    newPoints.Add(newPoints.First());
                }
            }
            for (int i = 0; i < newPoints.Count; i++)
            {
                newPolyline.AddVertexAt(i, newPoints[i], 0, 0, 0);
            }
            return newPolyline;
        }

        public static List<Polyline> RegionToPolylines(Region region)
        {
            var ret = new List<Polyline>();

            var entities = new DBObjectCollection();
            region.Explode(entities);

            var entityList = new List<Autodesk.AutoCAD.DatabaseServices.DBObject>();
            for (int i = 0; i < entities.Count; i++)
            {
                entityList.Add(entities[i]);
            }

            if (entityList[0].GetType() == typeof(Line))
            {
                var lines = entityList.Cast<Line>().ToList();
                var newPolyline = new Polyline();
                var newPoints = new List<Point3d>();

                var firstPoint = lines.First().StartPoint;
                var currentPoint = lines.First().EndPoint;
                newPoints.Add(firstPoint);
                newPoints.Add(currentPoint);
                lines.RemoveAt(0);
                while (lines.Count > 0)
                {
                    var nextLine = lines.Find(l => l.StartPoint == currentPoint || l.EndPoint == currentPoint);
                    if (nextLine.StartPoint == currentPoint)
                    {
                        newPoints.Add(nextLine.EndPoint);
                        currentPoint = nextLine.EndPoint;
                    }
                    else
                    {
                        newPoints.Add(nextLine.StartPoint);
                        currentPoint = nextLine.StartPoint;
                    }
                    lines.Remove(nextLine);
                }
                for (int i = 0; i < newPoints.Count; i++)
                {
                    newPolyline.AddVertexAt(i, new Point2d(newPoints[i].X, newPoints[i].Y), 0, 0, 0);
                }
                ret.Add(newPolyline);
                for (int i = 0; i < entities.Count; i++)
                {
                    entities[i].Dispose();
                }
                return ret;
            }
            else if (entityList[0].GetType() == typeof(Region))
            {
                ret.AddRange(entityList.Cast<Region>().SelectMany(RegionToPolylines));
                for (int i = 0; i < entities.Count; i++)
                {
                    entities[i].Dispose();
                }
                return ret;
            }
            else
            {
                throw new Exception($"Unexpected entity type {entityList[0].GetType().Name}");
            }
        }

        public static List<Polyline> IntersectRegions(Polyline boundingRectangle, Polyline polyline, Transaction transaction)
        {
            var polylineCurves = new DBObjectCollection();
            polylineCurves.Add(polyline);

            try
            {
                using var polylineRegion = Region.CreateFromCurves(polylineCurves).Cast<Region>().Single();

                var boundingRectangleCurves = new DBObjectCollection();
                boundingRectangleCurves.Add(boundingRectangle);
                using var boundingRectangleRegion = Region.CreateFromCurves(boundingRectangleCurves).Cast<Region>().Single();

                polylineRegion.BooleanOperation(BooleanOperationType.BoolIntersect, boundingRectangleRegion);

                return RegionToPolylines(polylineRegion);
            }
            catch (System.Exception)
            {
                return new List<Polyline>();
            }
        }

        public static void SetPgSurfaceBoundary(List<ObjectId> boundingRectangleIds, List<ObjectId> boundaryIds, Transaction transaction)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var boundingRectangles = boundingRectangleIds.Select(r => transaction.GetObject(r, OpenMode.ForRead) as Polyline);
            var pgSurfaceId = AutoCAD.Utils.GetTinSurface("PG", transaction);
            var pgSurface = transaction.GetObject(pgSurfaceId, OpenMode.ForWrite) as TinSurface;

            var x1 = boundingRectangles.Min(r => r.Bounds.Value.MinPoint.X);
            var x2 = boundingRectangles.Max(r => r.Bounds.Value.MaxPoint.X);
            var y1 = boundingRectangles.Min(r => r.Bounds.Value.MinPoint.Y);
            var y2 = boundingRectangles.Max(r => r.Bounds.Value.MaxPoint.Y);

            var offset = 1000;

            var offsetRectangle = new Polyline();
            offsetRectangle.AddVertexAt(offsetRectangle.NumberOfVertices, new Point2d(x1 - offset, y1 - offset), 0, 0, 0);
            offsetRectangle.AddVertexAt(offsetRectangle.NumberOfVertices, new Point2d(x1 - offset, y2 + offset), 0, 0, 0);
            offsetRectangle.AddVertexAt(offsetRectangle.NumberOfVertices, new Point2d(x2 + offset, y2 + offset), 0, 0, 0);
            offsetRectangle.AddVertexAt(offsetRectangle.NumberOfVertices, new Point2d(x2 + offset, y1 - offset), 0, 0, 0);
            offsetRectangle.AddVertexAt(offsetRectangle.NumberOfVertices, new Point2d(x1 - offset, y1 - offset), 0, 0, 0);

            var previousCurrentLayerId = doc.Database.Clayer;
            var pgHideBoundaryLayerName = "_PG-HIDE-BOUNDARY";
            var pgHideBoundaryLayerId = AutoCAD.Utils.GetOrCreateLayer(pgHideBoundaryLayerName, transaction);
            var pgHideBoundaryLayer = transaction.GetObject(pgHideBoundaryLayerId, OpenMode.ForWrite) as LayerTableRecord;
            doc.Database.Clayer = pgHideBoundaryLayerId;

            var blockTable = transaction.GetObject(doc.Database.BlockTableId, OpenMode.ForRead) as BlockTable;
            var blockTableRecordId = blockTable[BlockTableRecord.ModelSpace];
            var blockTableRecord = transaction.GetObject(blockTableRecordId, OpenMode.ForWrite) as BlockTableRecord;

            blockTableRecord.AppendEntity(offsetRectangle);
            transaction.AddNewlyCreatedDBObject(offsetRectangle, true);

            doc.Database.Clayer = previousCurrentLayerId;

            var pgHideBoundary = pgSurface.BoundariesDefinition.AddBoundaries(
                new ObjectIdCollection(new ObjectId[] { offsetRectangle.ObjectId }),
                1,
                SurfaceBoundaryType.Hide,
                true
            );
            pgHideBoundary.Name = "PG-HIDE";

            var pgShowBoundary = pgSurface.BoundariesDefinition.AddBoundaries(
                new ObjectIdCollection(boundaryIds.ToArray()),
                1,
                SurfaceBoundaryType.Show,
                true
            );
            pgShowBoundary.Name = "PG-SHOW";
        }

        public class ToleranceContext
        {
            public ObjectId InputSurfaceId { get; set; }
            public Polyline BoundingRectangle { get; set; }
            public List<List<Point3d>> PointGrid { get; set; }
            public List<List<Point3d>> LobfPoints { get; set; }
            public List<List<Point3d>> PgPoints { get; set; }
            public List<int> Modified { get; set; }

            public ToleranceContext(ObjectId inputSurfaceId,
                                    Polyline boundingRectangle,
                                    List<List<Point3d>> pointGrid,
                                    List<List<Point3d>> lobfPoints,
                                    List<List<Point3d>> pgPoints,
                                    List<int> modified)
            {
                InputSurfaceId = inputSurfaceId;
                BoundingRectangle = boundingRectangle;
                PointGrid = pointGrid;
                LobfPoints = lobfPoints;
                PgPoints = pgPoints;
                Modified = modified;
            }
        }

        public static ToleranceContext GetToleranceContext(LobfOptions options, ObjectId inputSurfaceId, ObjectId boundingRectangleId, Transaction transaction)
        {
            var boundingRectangle = transaction.GetObject(boundingRectangleId, OpenMode.ForWrite) as Polyline;

            var pointGrid = AutoCAD.Utils.CreatePointGrid(options.TrackerSpacing, options.TrackerWidth, boundingRectangleId, inputSurfaceId, transaction);
            var bestFitPoints = pointGrid.Select(BestFit).ToList();
            var pgPoints = pointGrid.Zip(bestFitPoints, (points, bestFitPoints) => PG(points, bestFitPoints)).ToList();
            var modified = new List<int>(bestFitPoints.Count);

            for (int i = 0; i < bestFitPoints.Count; ++i)
                modified.Add(0);
            return new ToleranceContext(inputSurfaceId, boundingRectangle, pointGrid, bestFitPoints, pgPoints,modified);
        }
        public static List<List<int>> FindMatchRecurse(int tolInd, int colInd, List<ToleranceContext> toleranceContexts, List<List<int>> connections)
        {
            if ((tolInd+1) >= (toleranceContexts.Count))
            {
                connections.Add(new List<int> { -1, -1, -1 });
                return connections;
            }
            var doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            var a = toleranceContexts[tolInd].LobfPoints;
            var b = toleranceContexts[tolInd+1].LobfPoints;

            var totalNumberOfRows = b.Count;

            var tolerance = 1;
            var noRoadDist = 15;
            var startIndex = 0;

            startIndex = b.FindIndex(row => AutoCAD.Utils.EqualWithinTolerance(row[0].X, a[colInd][0].X, tolerance));
            if (startIndex == -1)
            {
                //no match, empty space above, do nothing
                //ed.WriteMessage("no match, empty space\n");
                connections.Add(new List<int> { -1, -1, -1 });
                return connections;
            }
            var deltaYBetweenEnds = Math.Abs(a[colInd].Last().Y - b[startIndex][0].Y);
            if (deltaYBetweenEnds < noRoadDist)
            {
                //no road between match
                //ed.WriteMessage("no road between match\n");
                connections.Add(new List<int> { 1, tolInd + 1, startIndex });
                return FindMatchRecurse(tolInd+1,startIndex,toleranceContexts,connections);
            }
            //road exists
            //ed.WriteMessage("road exists between match\n");
            connections.Add(new List<int> { 2, tolInd + 1, startIndex });
            return FindMatchRecurse(tolInd + 1, startIndex, toleranceContexts, connections);
        }
        public static List<(List<Point3d>, List<Point3d>)> MatchRows(List<List<Point3d>> a, List<List<Point3d>> b)
        {
            var totalNumberOfRows = Math.Max(a.Count, b.Count);

            var tolerance = 1;

            var startIndexA = 0;
            var startIndexB = 0;

            startIndexB = b.FindIndex(row => AutoCAD.Utils.EqualWithinTolerance(row[0].X, a[0][0].X, tolerance));
            if (startIndexB == -1)
            {
                startIndexA = a.FindIndex(row => AutoCAD.Utils.EqualWithinTolerance(row[0].X, b[0][0].X, tolerance));
                if (startIndexA == -1)
                {
                    throw new Exception("Could not find matching rows");
                }
                startIndexB = 0;
            }

            var matchedRows = new List<(List<Point3d>, List<Point3d>)>();
            for (int i = 0; i < totalNumberOfRows; i++)
            {
                var rowA = i < startIndexB || i - startIndexB >= a.Count ? null : a[i - startIndexB];
                var rowB = i < startIndexA || i - startIndexA >= b.Count ? null : b[i - startIndexA];
                matchedRows.Add((rowA, rowB));
            }
            var doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            ed.WriteMessage("A index: "+startIndexA.ToString());
            ed.WriteMessage("B index: " + startIndexB.ToString());
            return matchedRows;
        }

        public static (List<Point3d>, List<Point3d>) TransformRowPair(List<Point3d> rowA, List<Point3d> rowB, double maximumVerticalDistance)
        {
            
            var deltaZBetweenEnds = rowA.Last().Z - rowB[0].Z;
            

            if (Math.Abs(deltaZBetweenEnds) <= maximumVerticalDistance)
            {
                return (rowA, rowB);
            }
            var ammountToMoveEndsBy = (Math.Abs(deltaZBetweenEnds) - maximumVerticalDistance) / 2;

            var newAEndZ = rowA.Last().Z + (deltaZBetweenEnds < 0 ? ammountToMoveEndsBy : -ammountToMoveEndsBy);
            var newAZStep = (newAEndZ - rowA[0].Z) / (rowA.Count - 1);

            var newRowA = new List<Point3d>();
            for (int i = 0; i < rowA.Count; i++)
            {
                var newPointA = new Point3d(rowA[i].X, rowA[i].Y, rowA[0].Z + newAZStep * i);
                newRowA.Add(newPointA);
            }

            var newBStartZ = rowB[0].Z + (deltaZBetweenEnds < 0 ? -ammountToMoveEndsBy : ammountToMoveEndsBy);
            var newBZStep = (rowB.Last().Z - newBStartZ) / (rowB.Count - 1);

            var newRowB = new List<Point3d>();
            for (int i = 0; i < rowB.Count; i++)
            {
                var newPointB = new Point3d(rowB[i].X, rowB[i].Y, newBStartZ + newBZStep * i);
                newRowB.Add(newPointB);
            }

            return (newRowA, newRowB);

        }

        public static (List<List<Point3d>>, List<List<Point3d>>) TransformMatchedRows(List<(List<Point3d>, List<Point3d>)> rowPairs, double maximumVerticalDistance)
        {
            var transformedRowsA = new List<List<Point3d>>();
            var transformedRowsB = new List<List<Point3d>>();
            foreach (var (rowA, rowB) in rowPairs)
            {
                if (rowA != null && rowB != null)
                {
                    var (newRowA, newRowB) = TransformRowPair(rowA, rowB, maximumVerticalDistance);
                    transformedRowsA.Add(newRowA);
                    transformedRowsB.Add(newRowB);
                }
                else
                {
                    if (rowA != null)
                    {
                        transformedRowsA.Add(rowA);
                    }
                    else if (rowB != null)
                    {
                        transformedRowsB.Add(rowB);
                    }
                }
            }
            return (transformedRowsA, transformedRowsB);
        }

        public static List<List<Point3d>> dissolveMergedSections(List<List<Point3d>> oldSections)
        {
            var newSections = new List<List<Point3d>>();
            for (var i = 0; i < oldSections.Count; i++)
            {
                if (oldSections[i].Count == 21)
                {
                    newSections.Add(oldSections[i]);
                }
                else
                {
                    var tempSections = oldSections[i].ChunkBy(21);
                    for (int k = 0; k < tempSections.Count(); k ++)
                    {
                        newSections.Add(tempSections[k]);
                    }
                }
            }
            return newSections;
        }
        public static List<List<Point3d>> connectNoRoadSections(List<List<int>> connections, List<List<Point3d>> oldPoints)
        {
            var noRoadconnections = new List<int>();
            var newPoints = new List<List<Point3d>>();
            for (int i = 0; i < connections.Count-1; i++)
            {
                if (connections[i][0] == 1 | (connections[i+1][0] == 1 & noRoadconnections.Count == 0))
                {
                    noRoadconnections.Add(i);
                }
                else if (noRoadconnections.Count > 0)
                {
                    var pointsToLobf = new List<Point3d>();
                    for (int j = 0; j < noRoadconnections.Count; j++)
                    {
                        pointsToLobf.AddRange(oldPoints[j]);
                    }
                    newPoints.Add(BestFit(pointsToLobf));
                    noRoadconnections.Clear();
                    if (connections[i + 1][0] == 1)
                    {
                        noRoadconnections.Add(i);
                    }
                    else
                    {
                        newPoints.Add(oldPoints[i]);
                    }             
                }
                else
                {
                    newPoints.Add(oldPoints[i]);
                }
            }
            if (noRoadconnections.Count > 0)
            {
                var pointsToLobf = new List<Point3d>();
                for (int j = 0; j < noRoadconnections.Count; j++)
                {
                    pointsToLobf.AddRange(oldPoints[j]);
                }
                newPoints.Add(BestFit(pointsToLobf));
            }
            return newPoints;
        }

        public static List<List<Point3d>> getPointsFromConnections(List<List<int>> connections, List<ToleranceContext> toleranceContexts)
        {
            var lobfPoints = new List<List<Point3d>>();
            for (int i = 0; i < connections.Count; i++)
            {
                if (connections[i][0] == -1)
                {
                    continue;
                }
                var temp = connections[i];
                if (temp[0] == 1)
                {
                    lobfPoints.Add(toleranceContexts[temp[1]].PointGrid[temp[2]]);
                }
                else
                {
                    lobfPoints.Add(toleranceContexts[temp[1]].LobfPoints[temp[2]]);
                }
            }
            return lobfPoints;
        }

        public static List<ToleranceContext> AdjustTolerances(List<ToleranceContext> toleranceContexts, double maximumVerticalDistance)
        {
            var toleranceContextsCopy = toleranceContexts.ToList();
            //var connections = new List<List<(int, int, int)>>();
            if (toleranceContextsCopy.Count >= 2)
            {
                for (int i = 0; i < toleranceContexts.Count - 1; i++)
                {
                    for (int j = 0; j < toleranceContexts[i].LobfPoints.Count - 1; j++)
                    {
                        if (toleranceContextsCopy[i].Modified[j] == 0)
                        {
                            var startConnections = new List<List<int>>();
                            startConnections.Add(new List<int> { 0, i, j });
                            var connection = FindMatchRecurse(i, j, toleranceContexts, startConnections);
                            var newSections = connectNoRoadSections(connection, getPointsFromConnections(connection, toleranceContexts));

                            for (var r = 0; r < newSections.Count - 1; r++)
                            {
                                var (transformedRowsA, transformedRowsB) = TransformRowPair(newSections[r], newSections[r + 1], maximumVerticalDistance);
                                newSections[r] = transformedRowsA;
                                newSections[r + 1] = transformedRowsB;

                            }
                            var dissolvedSections = dissolveMergedSections(newSections);
                            for (var c = 0; c < connection.Count - 1; c++)
                            {
                                var cur = connection[c];
                                toleranceContextsCopy[cur[1]].LobfPoints[cur[2]] = dissolvedSections[c];
                                toleranceContextsCopy[cur[1]].Modified[cur[2]] = 1;
                            }
                        }
                    }
                    toleranceContextsCopy[i].PgPoints =
                        toleranceContextsCopy[i]
                            .PointGrid
                            .Zip(toleranceContextsCopy[i].LobfPoints, (points, bestFitPoints) => PG(points, bestFitPoints))
                            .ToList();
                }
            }
                return toleranceContextsCopy;
        }

        public static List<Polyline> LobfCommand(LobfOptions options, ToleranceContext toleranceContext, Transaction transaction)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var previousCurrentLayerId = doc.Database.Clayer;

            var noDisplaySurfaceStyleId = AutoCAD.Utils.GetOrCreateSurfaceStyle("_No Display", transaction);
            var noDisplaySurfaceStyle = transaction.GetObject(noDisplaySurfaceStyleId, OpenMode.ForWrite) as SurfaceStyle;
            foreach (SurfaceDisplayStyleType surfaceDisplayStyleType in Enum.GetValues(typeof(SurfaceDisplayStyleType)))
            {
                noDisplaySurfaceStyle.GetDisplayStylePlan(surfaceDisplayStyleType).Visible = false;
                noDisplaySurfaceStyle.GetDisplayStyleModel(surfaceDisplayStyleType).Visible = false;
            }

            var boundingRectangle = toleranceContext.BoundingRectangle;

            var bestFitLayerName = "_LOBF";
            var bestFitLayerId = AutoCAD.Utils.GetOrCreateLayer(bestFitLayerName, transaction);
            var bestFitLayer = transaction.GetObject(bestFitLayerId, OpenMode.ForWrite) as LayerTableRecord;
            bestFitLayer.IsFrozen = false;
            doc.Database.Clayer = bestFitLayerId;

            var bestFitPoints = toleranceContext.LobfPoints.SelectMany(x => x).ToArray();
            var bestFitCollection = new Point3dCollection(bestFitPoints);
            CivilApplication.ActiveDocument.CogoPoints.Add(bestFitCollection, "LOBF", true, false, true);
            var bestFitPointGroupId = AutoCAD.Utils.GetOrCreatePointGroup("LOBF");
            var bestFitPointGroup = transaction.GetObject(bestFitPointGroupId, OpenMode.ForWrite) as PointGroup;
            var bestFitPointGroupQuery = new StandardPointGroupQuery();
            bestFitPointGroupQuery.IncludeFullDescriptions = "LOBF";
            bestFitPointGroup.SetQuery(bestFitPointGroupQuery);

            var extendedBestFitPoints = toleranceContext.LobfPoints.SelectMany(ExtendPoints).ToArray();

            var bestFitSurfaceId = TinSurface.Create("LOBF", noDisplaySurfaceStyleId);
            var bestFitSurface = transaction.GetObject(bestFitSurfaceId, OpenMode.ForWrite) as TinSurface;
            bestFitSurface.AddVertices(new Point3dCollection(extendedBestFitPoints));

            doc.Database.Clayer = previousCurrentLayerId;
            bestFitLayer.IsFrozen = true;

            var pgSurfaceLayerName = "Z-SURF-PG";
            var pgSurfaceLayerId = AutoCAD.Utils.GetOrCreateLayer(pgSurfaceLayerName, transaction);
            doc.Database.Clayer = pgSurfaceLayerId;

            var pgPoints = toleranceContext.PgPoints.SelectMany(x => x).ToArray();

            var pgSurfaceId = AutoCAD.Utils.GetOrCreateTinSurface("PG", noDisplaySurfaceStyleId, transaction);
            var pgSurface = transaction.GetObject(pgSurfaceId, OpenMode.ForWrite) as TinSurface;
            pgSurface.AutoRebuild = true;
            pgSurface.AddVertices(new Point3dCollection(pgPoints));

            doc.Database.Clayer = previousCurrentLayerId;

            var aboveBestFitSurfaceId = TinSurface.Create("AboveBestFit", noDisplaySurfaceStyleId);
            var aboveBestFitSurface = transaction.GetObject(aboveBestFitSurfaceId, OpenMode.ForWrite) as TinSurface;
            aboveBestFitSurface.AddVertices(new Point3dCollection(bestFitPoints.Select(p => new Point3d(p.X, p.Y, p.Z + options.Tolerance)).ToArray()));

            var belowBestFitSurfaceId = TinSurface.Create("BelowBestFit", noDisplaySurfaceStyleId);
            var belowBestFitSurface = transaction.GetObject(belowBestFitSurfaceId, OpenMode.ForWrite) as TinSurface;
            belowBestFitSurface.AddVertices(new Point3dCollection(bestFitPoints.Select(p => new Point3d(p.X, p.Y, p.Z - options.Tolerance)).ToArray()));

            var boundaryIds = AutoCAD.Utils.GetIntersectionOfSurfaces(toleranceContext.InputSurfaceId, belowBestFitSurfaceId, transaction);
            boundaryIds.AddRange(AutoCAD.Utils.GetIntersectionOfSurfaces(toleranceContext.InputSurfaceId, aboveBestFitSurfaceId, transaction));

            var newBoundaries = new List<Polyline>();

            foreach (var boundaryId in boundaryIds)
            {
                var boundary = transaction.GetObject(boundaryId, OpenMode.ForWrite);
                using var newBoundary = AutoCAD.Utils.ToLightweightPolyline(boundary, transaction);
                boundary.Erase();

                using var snappedBoundary = AutoCAD.Utils.SnapToBoundary(toleranceContext.BoundingRectangle, newBoundary, transaction);

                using var trimmedBoundary = TrimExterior(toleranceContext.BoundingRectangle, snappedBoundary, transaction);

                using var closedBoundary = CloseRegion(boundingRectangle, trimmedBoundary, transaction);

                var intersectedBoundaries = IntersectRegions(boundingRectangle, closedBoundary, transaction);

                var simplifiedBoundaries =
                    intersectedBoundaries
                        .Select(b => AutoCAD.Utils.WeedVertices(boundingRectangle, b, transaction))
                        .ToList();

                intersectedBoundaries.ForEach(b => b.Dispose());

                foreach (var simplifiedBoundary in simplifiedBoundaries)
                {
                    simplifiedBoundary.Closed = true;
                    if (simplifiedBoundary.Area >= options.MinimumBoundaryArea)
                    {
                        newBoundaries.Add(simplifiedBoundary);
                    }
                    else
                    {
                        simplifiedBoundary.Dispose();
                    }
                }

            }

            var boundariesToRemove = new List<Polyline>();
            foreach (var boundaryA in newBoundaries)
            {
                foreach (var boundaryB in newBoundaries)
                {
                    if (boundaryA != boundaryB)
                    {
                        if (AutoCAD.Utils.IsInside(boundaryA, boundaryB, transaction))
                        {
                            boundariesToRemove.Add(boundaryB);
                        }
                    }
                }
            }

            foreach (var boundary in boundariesToRemove)
            {
                newBoundaries.Remove(boundary);
                boundary.Dispose();
            }

            var cutFillBoundaryLayerName = "Z-SURF-CTFL";
            var cutFillBoundaryLayerId = AutoCAD.Utils.GetOrCreateLayer(cutFillBoundaryLayerName, transaction);
            var cutFillBoundaryLayer = transaction.GetObject(cutFillBoundaryLayerId, OpenMode.ForWrite) as LayerTableRecord;

            var blockTable = transaction.GetObject(doc.Database.BlockTableId, OpenMode.ForRead) as BlockTable;
            var blockTableRecordId = blockTable[BlockTableRecord.ModelSpace];
            var blockTableRecord = transaction.GetObject(blockTableRecordId, OpenMode.ForWrite) as BlockTableRecord;

            foreach (Autodesk.AutoCAD.DatabaseServices.Entity entity in newBoundaries)
            {
                entity.LayerId = cutFillBoundaryLayerId;
                blockTableRecord.AppendEntity(entity);
                transaction.AddNewlyCreatedDBObject(entity, true);
            }

            aboveBestFitSurface.Erase();
            belowBestFitSurface.Erase();

            return newBoundaries;
        }

        public static void ToleranceCommand()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            using (Transaction transaction = doc.Database.TransactionManager.StartTransaction())
            {
                var lobfOptions = new LobfOptions();

                var trackerSpacingPromptOptions = new PromptDistanceOptions("\nTracker spacing");
                trackerSpacingPromptOptions.DefaultValue = lobfOptions.TrackerSpacing;
                trackerSpacingPromptOptions.AllowNegative = false;
                var trackerSpacingPromptResult = doc.Editor.GetDistance(trackerSpacingPromptOptions);
                if (trackerSpacingPromptResult.Status != PromptStatus.OK)
                {
                    return;
                }
                lobfOptions.TrackerSpacing = trackerSpacingPromptResult.Value;

                var trackerWidthPromptOptions = new PromptDistanceOptions("\nTracker width");
                trackerWidthPromptOptions.DefaultValue = lobfOptions.TrackerWidth;
                trackerWidthPromptOptions.AllowNegative = false;
                var trackerWidthPromptResult = doc.Editor.GetDistance(trackerWidthPromptOptions);
                if (trackerWidthPromptResult.Status != PromptStatus.OK)
                {
                    return;
                }
                lobfOptions.TrackerWidth = trackerWidthPromptResult.Value;

                var maximumVerticalDistanceBetweenAdjacentRowsPromptOptions = new PromptDistanceOptions("\nMaximum vertical distance between adjacent rows");
                maximumVerticalDistanceBetweenAdjacentRowsPromptOptions.DefaultValue = lobfOptions.MaximumVerticalDistanceBetweenAdjacentRows;
                maximumVerticalDistanceBetweenAdjacentRowsPromptOptions.AllowNegative = false;
                var maximumVerticalDistanceBetweenAdjacentRowsPromptResult = doc.Editor.GetDistance(maximumVerticalDistanceBetweenAdjacentRowsPromptOptions);
                if (maximumVerticalDistanceBetweenAdjacentRowsPromptResult.Status != PromptStatus.OK)
                {
                    return;
                }
                lobfOptions.MaximumVerticalDistanceBetweenAdjacentRows = maximumVerticalDistanceBetweenAdjacentRowsPromptResult.Value;

                var tolerancePromptOptions = new PromptDistanceOptions("\nTolerance");
                tolerancePromptOptions.DefaultValue = lobfOptions.Tolerance;
                tolerancePromptOptions.AllowNegative = false;
                var tolerancePromptResult = doc.Editor.GetDistance(tolerancePromptOptions);
                if (tolerancePromptResult.Status != PromptStatus.OK)
                {
                    return;
                }
                lobfOptions.Tolerance = tolerancePromptResult.Value;

                var minimumBoundaryAreaPromptOptions = new PromptDistanceOptions("\nMinimum boundary area");
                minimumBoundaryAreaPromptOptions.DefaultValue = lobfOptions.MinimumBoundaryArea;
                minimumBoundaryAreaPromptOptions.AllowNegative = false;
                var minimumBoundaryAreaPromptResult = doc.Editor.GetDistance(minimumBoundaryAreaPromptOptions);
                if (minimumBoundaryAreaPromptResult.Status != PromptStatus.OK)
                {
                    return;
                }
                lobfOptions.MinimumBoundaryArea = minimumBoundaryAreaPromptResult.Value;

                var inputSurfacePromptOptions = new PromptEntityOptions("\nSelect input surface");
                inputSurfacePromptOptions.SetRejectMessage("\nThe selected object is not a TIN Surface.");
                inputSurfacePromptOptions.AddAllowedClass(typeof(Autodesk.Civil.DatabaseServices.TinSurface), true);
                inputSurfacePromptOptions.AllowNone = false;
                var inputSurfacePromptResult = doc.Editor.GetEntity(inputSurfacePromptOptions);
                if (inputSurfacePromptResult.Status != PromptStatus.OK)
                {
                    return;
                }

                var boundingRectanglePromptOptions = new PromptSelectionOptions();
                boundingRectanglePromptOptions.MessageForAdding = "\nSelect bounding rectangle(s)";
                var boundingRectangleSelectionResult = doc.Editor.GetSelection(boundingRectanglePromptOptions);
                if (boundingRectangleSelectionResult.Status != PromptStatus.OK)
                {
                    return;
                }

                var boundingRectangleIds = boundingRectangleSelectionResult.Value.GetObjectIds().ToList();
                foreach (var boundingRectangleId in boundingRectangleIds)
                {
                    if (!AutoCAD.Utils.IsRectangle(boundingRectangleId, transaction))
                    {
                        doc.Editor.WriteMessage("\nOnly rectangular polylines are allowed as bounding regions");
                        return;
                    }
                }

                doc.TransactionManager.EnableGraphicsFlush(false);
                //var MergedBoundingRectangleIds = CheckForRoadsAndMerge(boundingRectangleIds,transaction);
                var toleranceContexts =
                    boundingRectangleIds
                        .Select(boundingRectangleId => transaction.GetObject(boundingRectangleId, OpenMode.ForRead) as Polyline)
                        .OrderBy(boundingRectangle => boundingRectangle.Bounds.Value.MinPoint.Y)
                        .Select(boundingRectangle => GetToleranceContext(lobfOptions, inputSurfacePromptResult.ObjectId, boundingRectangle.ObjectId, transaction))
                        .ToList();

                var adjustedToleranceContexts = AdjustTolerances(toleranceContexts, lobfOptions.MaximumVerticalDistanceBetweenAdjacentRows);

                var boundaries = new List<Polyline>();
                foreach (var toleranceContext in adjustedToleranceContexts)
                {
                    boundaries.AddRange(LobfCommand(lobfOptions, toleranceContext, transaction));
                }

                SetPgSurfaceBoundary(boundingRectangleIds, boundaries.Select(b => b.ObjectId).ToList(), transaction);

                boundaries.ForEach(b => b.Dispose());

                doc.TransactionManager.EnableGraphicsFlush(true);
                doc.TransactionManager.FlushGraphics();
                doc.Editor.Regen();
                transaction.Commit();
            }
        }
    }
}
