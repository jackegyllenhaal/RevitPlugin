using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RevitPlugin
{
    public partial class HelloRevit
    {
        private static List<OpeningData> DetectOpenings(
            WallData wall)
        {
            List<Solid> solids =
                GetWallSolids(wall.Wall);

            if (solids.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Стена ID {wall.Wall.Id.IntegerValue}: не удалось получить геометрию бетона.");
            }

            return FindOpenings(
                wall,
                solids);
        }


        private static List<OpeningData> FindOpenings(
            WallData wall,
            List<Solid> solids)
        {
            List<OpeningData> result =
                new List<OpeningData>();

            double step =
                ToFeet(OpeningScanStepMm);

            List<GapData> allGaps =
                new List<GapData>();

            for (double z = wall.BottomZ + step;
                 z <= wall.TopZ - step;
                 z += step)
            {
                List<GapData> gaps =
                    FindGapsAtLevel(
                        wall,
                        solids,
                        z);

                foreach (GapData gap in gaps)
                {
                    GapData existing =
                        allGaps.FirstOrDefault(
                            x =>
                                Math.Abs(
                                    x.Left - gap.Left)
                                < ToFeet(5.0)
                                &&
                                Math.Abs(
                                    x.Right - gap.Right)
                                < ToFeet(5.0)
                                && z - x.MaxDetectedZ <= step + ToFeet(0.1));

                    if (existing == null)
                    {
                        gap.MinDetectedZ = z;
                        gap.MaxDetectedZ = z;

                        allGaps.Add(gap);
                    }
                    else
                    {
                        existing.MinDetectedZ =
                            Math.Min(
                                existing.MinDetectedZ,
                                z);

                        existing.MaxDetectedZ =
                            Math.Max(
                                existing.MaxDetectedZ,
                                z);
                    }
                }
            }

            foreach (GapData gap in allGaps)
            {
                double middleX =
                    (gap.Left + gap.Right) / 2.0;

                double bottom =
                    FindVerticalTransition(
                        wall,
                        solids,
                        middleX,
                        gap.MinDetectedZ,
                        -1);

                double top =
                    FindVerticalTransition(
                        wall,
                        solids,
                        middleX,
                        gap.MaxDetectedZ,
                        +1);

                if (top <= bottom)
                {
                    continue;
                }

                OpeningData opening =
                    new OpeningData
                    {
                        Left = gap.Left,
                        Right = gap.Right,
                        BottomZ = bottom,
                        TopZ = top,
                        LeftMm = ToMm(gap.Left),
                        RightMm = ToMm(gap.Right)
                    };

                bool duplicate =
                    result.Any(
                        x =>
                            Math.Abs(
                                x.Left - opening.Left)
                            < ToFeet(5.0)
                            &&
                            Math.Abs(
                                x.Right - opening.Right)
                            < ToFeet(5.0)
                            &&
                            Math.Abs(
                                x.BottomZ - opening.BottomZ)
                            < ToFeet(5.0)
                            &&
                            Math.Abs(
                                x.TopZ - opening.TopZ)
                            < ToFeet(5.0));

                if (!duplicate)
                {
                    result.Add(opening);
                }
            }

            return result;
        }


        private static List<GapData> FindGapsAtLevel(
            WallData wall,
            List<Solid> solids,
            double z)
        {
            List<GapData> gaps =
                new List<GapData>();

            double step =
                ToFeet(OpeningScanStepMm);

            bool previousConcrete =
                HasConcrete(
                    wall,
                    solids,
                    0.0,
                    z);

            double gapStart = -1.0;

            for (double x = step;
                 x < wall.Length + step;
                 x += step)
            {
                double testX =
                    Math.Min(
                        x,
                        wall.Length);

                bool concrete =
                    HasConcrete(
                        wall,
                        solids,
                        testX,
                        z);

                if (previousConcrete &&
                    !concrete)
                {
                    gapStart =
                        RefineHorizontalBoundary(
                            wall,
                            solids,
                            Math.Max(
                                0,
                                testX - step),
                            testX,
                            z,
                            true);
                }

                if (!previousConcrete &&
                    concrete &&
                    gapStart >= 0)
                {
                    double right =
                        RefineHorizontalBoundary(
                            wall,
                            solids,
                            Math.Max(
                                0,
                                testX - step),
                            testX,
                            z,
                            false);

                    if (gapStart > ToFeet(1.0) &&
                        right <
                        wall.Length - ToFeet(1.0))
                    {
                        gaps.Add(
                            new GapData
                            {
                                Left = gapStart,
                                Right = right
                            });
                    }

                    gapStart = -1.0;
                }

                previousConcrete = concrete;
            }

            return gaps;
        }


        private static double RefineHorizontalBoundary(
            WallData wall,
            List<Solid> solids,
            double low,
            double high,
            double z,
            bool concreteToVoid)
        {
            double tolerance =
                ToFeet(OpeningRefineToleranceMm);

            for (int i = 0;
                 i < 30 &&
                 high - low > tolerance;
                 i++)
            {
                double mid =
                    (low + high) / 2.0;

                bool concrete =
                    HasConcrete(
                        wall,
                        solids,
                        mid,
                        z);

                if (concreteToVoid)
                {
                    if (concrete)
                    {
                        low = mid;
                    }
                    else
                    {
                        high = mid;
                    }
                }
                else
                {
                    if (concrete)
                    {
                        high = mid;
                    }
                    else
                    {
                        low = mid;
                    }
                }
            }

            return (low + high) / 2.0;
        }


        private static double FindVerticalTransition(
            WallData wall,
            List<Solid> solids,
            double x,
            double detectedZ,
            int direction)
        {
            double step =
                ToFeet(OpeningScanStepMm);

            double outsideZ =
                detectedZ;

            if (HasConcrete(
                wall,
                solids,
                x,
                outsideZ))
            {
                return detectedZ;
            }

            while (true)
            {
                double next =
                    outsideZ
                    + step * direction;

                if (next <= wall.BottomZ)
                {
                    return wall.BottomZ;
                }

                if (next >= wall.TopZ)
                {
                    return wall.TopZ;
                }

                bool concrete =
                    HasConcrete(
                        wall,
                        solids,
                        x,
                        next);

                if (concrete)
                {
                    double low;
                    double high;

                    if (direction < 0)
                    {
                        low = next;
                        high = outsideZ;
                    }
                    else
                    {
                        low = outsideZ;
                        high = next;
                    }

                    double tolerance =
                        ToFeet(
                            OpeningRefineToleranceMm);

                    for (int i = 0;
                         i < 30 &&
                         high - low > tolerance;
                         i++)
                    {
                        double mid =
                            (low + high) / 2.0;

                        bool midConcrete =
                            HasConcrete(
                                wall,
                                solids,
                                x,
                                mid);

                        if (direction < 0)
                        {
                            if (midConcrete)
                            {
                                low = mid;
                            }
                            else
                            {
                                high = mid;
                            }
                        }
                        else
                        {
                            if (midConcrete)
                            {
                                high = mid;
                            }
                            else
                            {
                                low = mid;
                            }
                        }
                    }

                    return (low + high) / 2.0;
                }

                outsideZ = next;
            }
        }


        private static bool HasConcrete(
            WallData wall,
            List<Solid> solids,
            double position,
            double z)
        {
            XYZ center =
                wall.StartPoint
                + wall.Direction * position;

            center =
                new XYZ(
                    center.X,
                    center.Y,
                    z);

            double extension =
                wall.Thickness
                + ToFeet(200.0);

            XYZ p1 =
                center
                - wall.Normal * extension;

            XYZ p2 =
                center
                + wall.Normal * extension;

            Line line =
                Line.CreateBound(
                    p1,
                    p2);

            SolidCurveIntersectionOptions options =
                new SolidCurveIntersectionOptions();

            foreach (Solid solid in solids)
            {
                try
                {
                    SolidCurveIntersection intersection =
                        solid.IntersectWithCurve(
                            line,
                            options);

                    if (intersection != null &&
                        intersection.SegmentCount > 0)
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Стена ID {wall.Wall.Id.IntegerValue}: ошибка проверки геометрии проёма.", ex);
                }
            }

            return false;
        }


        private static List<Solid> GetWallSolids(
            Wall wall)
        {
            List<Solid> result =
                new List<Solid>();

            Options options =
                new Options();

            options.ComputeReferences = false;
            options.IncludeNonVisibleObjects = false;
            options.DetailLevel = ViewDetailLevel.Fine;

            GeometryElement geometry =
                wall.get_Geometry(options);

            if (geometry == null)
            {
                return result;
            }

            CollectSolids(
                geometry,
                Transform.Identity,
                result);

            return result;
        }


        private static void CollectSolids(
            GeometryElement geometry,
            Transform transform,
            List<Solid> result)
        {
            foreach (GeometryObject obj in geometry)
            {
                Solid solid =
                    obj as Solid;

                if (solid != null)
                {
                    if (solid.Volume > 1e-9)
                    {
                        result.Add(
                            SolidUtils.CreateTransformed(
                                solid,
                                transform));
                    }

                    continue;
                }

                GeometryInstance instance =
                    obj as GeometryInstance;

                if (instance != null)
                {
                    GeometryElement instanceGeometry =
                        instance.GetSymbolGeometry();

                    if (instanceGeometry == null)
                    {
                        continue;
                    }

                    Transform combined =
                        transform.Multiply(
                            instance.Transform);

                    CollectSolids(
                        instanceGeometry,
                        combined,
                        result);
                }
            }
        }


        private static List<WallData> ReadWalls(
            Document doc,
            AssemblyInstance assembly)
        {
            List<WallData> result =
                new List<WallData>();

            foreach (ElementId id in assembly.GetMemberIds())
            {
                Wall wall =
                    doc.GetElement(id) as Wall;

                if (wall == null)
                {
                    continue;
                }

                LocationCurve location =
                    wall.Location as LocationCurve;

                if (location == null)
                {
                    continue;
                }

                Curve curve =
                    location.Curve;

                if (curve == null)
                {
                    continue;
                }

                // ----------------------------------------------------
                // ПРОВЕРКА ПОДДЕРЖИВАЕМОЙ ГЕОМЕТРИИ СТЕНЫ
                //
                // Алгоритм армирования рассчитан на прямые
                // вертикальные стены. Проверяем причины отдельно,
                // чтобы сообщение точно показывало проблему.
                // ----------------------------------------------------

                if (!(curve is Line))
                {
                    throw new InvalidOperationException(
                        $"Стена ID {wall.Id.IntegerValue}: " +
                        $"ось стены имеет тип {curve.GetType().Name}. " +
                        "Поддерживаются только стены с прямой осью (Line).");
                }

                Parameter crossSection =
                    wall.get_Parameter(
                        BuiltInParameter.WALL_CROSS_SECTION);

                int crossSectionValue =
                    crossSection != null
                        ? crossSection.AsInteger()
                        : 0;

                Parameter slantAngle =
                    wall.get_Parameter(
                        BuiltInParameter.WALL_SINGLE_SLANT_ANGLE_FROM_VERTICAL);

                double slantAngleRad =
                    slantAngle != null
                        ? slantAngle.AsDouble()
                        : 0.0;

                double slantAngleDeg =
                    slantAngleRad * 180.0 / Math.PI;

                if (crossSectionValue != 0)
                {
                    throw new InvalidOperationException(
                        $"Стена ID {wall.Id.IntegerValue}: " +
                        $"Cross-Section = {crossSectionValue}, " +
                        $"угол наклона = {slantAngleDeg:F3}°. " +
                        "Для автоматического армирования установите " +
                        "Cross-Section = Vertical.");
                }

                if (Math.Abs(slantAngleRad) > 1e-9)
                {
                    throw new InvalidOperationException(
                        $"Стена ID {wall.Id.IntegerValue}: " +
                        $"угол наклона от вертикали = {slantAngleDeg:F3}°. " +
                        "Поддерживаются только вертикальные стены " +
                        "с углом наклона 0°.");
                }

                XYZ p0 =
                    curve.GetEndPoint(0);

                XYZ p1 =
                    curve.GetEndPoint(1);

                if (Math.Abs(p1.Z - p0.Z) > ToFeet(1.0))
                    throw new InvalidOperationException(
                        $"Стена ID {wall.Id.IntegerValue}: ось стены должна быть горизонтальной.");

                XYZ direction =
                    p1 - p0;

                direction =
                    new XYZ(
                        direction.X,
                        direction.Y,
                        0);

                if (direction.GetLength() < 1e-9)
                {
                    continue;
                }

                direction =
                    direction.Normalize();

                XYZ normal =
                    new XYZ(
                        -direction.Y,
                        direction.X,
                        0);

                BoundingBoxXYZ box =
                    wall.get_BoundingBox(null);

                if (box == null)
                {
                    continue;
                }

                WallType wallType =
                    doc.GetElement(
                        wall.GetTypeId())
                    as WallType;

                if (wallType == null)
                {
                    continue;
                }

                WallData data =
                    new WallData();

                data.Wall = wall;

                data.StartPoint =
                    new XYZ(
                        p0.X,
                        p0.Y,
                        0);

                data.EndPoint =
                    new XYZ(
                        p1.X,
                        p1.Y,
                        0);

                data.Direction = direction;
                data.Normal = normal;

                data.Length =
                    curve.Length;

                data.Thickness =
                    wallType.Width;

                data.BottomZ =
                    box.Min.Z;

                data.TopZ =
                    box.Max.Z;

                data.Openings =
                    new List<OpeningData>();

                result.Add(data);
            }

            return result;
        }


        // ============================================================
        // РАЗДЕЛЕНИЕ СТЕН НА ВЕРТИКАЛЬНЫЕ ВЕТВИ
        // ============================================================

        private static List<List<WallData>> BuildVerticalWallBranches(
            List<WallData> walls)
        {
            List<List<WallData>> branches =
                new List<List<WallData>>();

            if (walls == null ||
                walls.Count == 0)
            {
                return branches;
            }

            List<WallData> sortedWalls =
                walls
                    .OrderBy(w => w.BottomZ)
                    .ThenBy(w => w.StartPoint.X)
                    .ThenBy(w => w.StartPoint.Y)
                    .ToList();

            double directionTolerance = 0.01;
            double geometryTolerance = ToFeet(20.0);

            foreach (WallData wall in sortedWalls)
            {
                List<WallData> bestBranch = null;
                double bestDistance = double.MaxValue;

                foreach (List<WallData> branch in branches)
                {
                    if (branch.Count == 0)
                    {
                        continue;
                    }

                    WallData reference = branch[0];

                    WallData previous = branch[branch.Count - 1];
                    // Продолжение должно примыкать по высоте к последней стене.
                    if (wall.BottomZ <= previous.BottomZ + ToFeet(ZToleranceMm)
                        || Math.Abs(wall.BottomZ - previous.TopZ) > ToFeet(ZToleranceMm))
                        continue;

                    double dot =
                        Math.Abs(
                            wall.Direction.DotProduct(
                                reference.Direction));

                    if (Math.Abs(1.0 - dot) > directionTolerance)
                    {
                        continue;
                    }

                    // Проверяем оба конца: близости середин недостаточно для длинных стен.
                    double directEnds = Math.Max(
                        wall.StartPoint.DistanceTo(reference.StartPoint),
                        wall.EndPoint.DistanceTo(reference.EndPoint));
                    double reversedEnds = Math.Max(
                        wall.StartPoint.DistanceTo(reference.EndPoint),
                        wall.EndPoint.DistanceTo(reference.StartPoint));
                    if (Math.Min(directEnds, reversedEnds) > geometryTolerance)
                        continue;

                    XYZ wallMiddle =
                        wall.StartPoint
                        + wall.Direction * (wall.Length / 2.0);

                    XYZ referenceMiddle =
                        reference.StartPoint
                        + reference.Direction * (reference.Length / 2.0);

                    double middleDistance =
                        new XYZ(
                            wallMiddle.X - referenceMiddle.X,
                            wallMiddle.Y - referenceMiddle.Y,
                            0)
                        .GetLength();

                    double lengthDifference =
                        Math.Abs(
                            wall.Length - reference.Length);

                    if (middleDistance > geometryTolerance ||
                        lengthDifference > geometryTolerance)
                    {
                        continue;
                    }

                    if (middleDistance < bestDistance)
                    {
                        bestDistance = middleDistance;
                        bestBranch = branch;
                    }
                }

                if (bestBranch == null)
                {
                    List<WallData> newBranch =
                        new List<WallData>();

                    newBranch.Add(wall);
                    branches.Add(newBranch);
                }
                else
                {
                    bestBranch.Add(wall);
                }
            }

            foreach (List<WallData> branch in branches)
            {
                branch.Sort(
                    (a, b) =>
                        a.BottomZ.CompareTo(
                            b.BottomZ));
            }

            return branches;
        }


        private static ConcreteInfo FindConcreteClass(
            Document doc,
            List<WallData> walls)
        {
            ConcreteInfo result = null;
            foreach (WallData wall in walls)
            {
                string concrete =
                    TryGetConcreteClass(
                        doc,
                        wall.Wall);

                var classes = new[]
                {
                    new ConcreteInfo { Name = "C20/25", LapFactor = 65.0 },
                    new ConcreteInfo { Name = "C25/30", LapFactor = 55.0 },
                    new ConcreteInfo { Name = "C30/37", LapFactor = 50.0 }
                }.Where(x => !string.IsNullOrEmpty(concrete) &&
                    concrete.IndexOf(x.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                if (classes.Count != 1)
                    throw new InvalidOperationException(
                        $"Стена ID {wall.Wall.Id.IntegerValue}: класс бетона не определён однозначно. " +
                        "Укажите один из C20/25, C25/30, C30/37.");

                if (result != null && result.Name != classes[0].Name)
                    throw new InvalidOperationException(
                        "В сборке разные классы бетона. Армируйте стены каждого класса отдельной сборкой.");

                result = classes[0];
            }

            return result;
        }


        private static string TryGetConcreteClass(
            Document doc,
            Wall wall)
        {
            StringBuilder text =
                new StringBuilder();

            text.AppendLine(wall.Name);

            foreach (Parameter p in wall.Parameters)
            {
                string value =
                    GetParameterText(p);

                if (!string.IsNullOrWhiteSpace(value))
                {
                    text.AppendLine(value);
                }
            }

            Element type =
                doc.GetElement(
                    wall.GetTypeId());

            if (type != null)
            {
                text.AppendLine(type.Name);

                foreach (Parameter p in type.Parameters)
                {
                    string value =
                        GetParameterText(p);

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        text.AppendLine(value);
                    }
                }
            }

            return text.ToString();
        }


        private static string GetParameterText(
            Parameter parameter)
        {
            if (parameter == null ||
                !parameter.HasValue)
            {
                return "";
            }

            try
            {
                if (parameter.StorageType ==
                    StorageType.String)
                {
                    return parameter.AsString() ?? "";
                }

                return parameter.AsValueString() ?? "";
            }
            catch
            {
                return "";
            }
        }


        private static double ToMm(
            double feet)
        {
            return UnitUtils.ConvertFromInternalUnits(
                feet,
                UnitTypeId.Millimeters);
        }


        private static double ToFeet(
            double millimeters)
        {
            return UnitUtils.ConvertToInternalUnits(
                millimeters,
                UnitTypeId.Millimeters);
        }
    }

// ================================================================
    // DATA
    // ================================================================

    public class WallData
    {
        public Wall Wall { get; set; }

        public XYZ StartPoint { get; set; }

        public XYZ EndPoint { get; set; }

        public XYZ Direction { get; set; }

        public XYZ Normal { get; set; }

        public double Length { get; set; }

        public double Thickness { get; set; }

        public double BottomZ { get; set; }

        public double TopZ { get; set; }

        public List<OpeningData> Openings { get; set; }

        // Горизонтальная арматура в Г-образном узле:
        // насколько ось стержня выходит за START / END стены
        // в тело соседней стены.
        public double StartHorizontalExtension { get; set; }

        public double EndHorizontalExtension { get; set; }
    }


    public class OpeningData
    {
        public double Left { get; set; }

        public double Right { get; set; }

        public double BottomZ { get; set; }

        public double TopZ { get; set; }

        public double LeftMm { get; set; }

        public double RightMm { get; set; }
    }


    public class GapData
    {
        public double Left { get; set; }

        public double Right { get; set; }

        public double MinDetectedZ { get; set; }

        public double MaxDetectedZ { get; set; }
    }


    public class VerticalRange
    {
        public double Bottom { get; set; }

        public double Top { get; set; }
    }


    public class HorizontalRange
    {
        public double Start { get; set; }

        public double End { get; set; }
    }


    public class ConcreteInfo
    {
        public string Name { get; set; }

        public double LapFactor { get; set; }
    }
}
