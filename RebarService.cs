using Autodesk.Revit.DB;

using Autodesk.Revit.DB.Structure;



using System;

using System.Collections.Generic;

using System.Linq;



namespace RevitPlugin

{

    public partial class HelloRevit

    {

        // ============================================================

        // ОСНОВНАЯ ВЕРТИКАЛЬНАЯ АРМАТУРА

        // ============================================================



        private static int CreateVerticalMainBars(

            Document doc,

            List<WallData> walls,

            List<WallData> allWalls,

            RebarBarType barType,

            double lapMm)

        {

            int count = 0;



            double lap =

                ToFeet(lapMm);



            for (

                int wallIndex = 0;

                wallIndex < walls.Count;

                wallIndex++)

            {

                WallData wall =

                    walls[wallIndex];



                bool hasNext =

                    wallIndex < walls.Count - 1;



                double barBottom =

                    wall.BottomZ;



                double barTop;



                // ----------------------------------------------------

                // ЕСТЬ СТЕНА ВЫШЕ

                //

                // Сохраняем существующий нахлёст.

                // ----------------------------------------------------



                if (hasNext)

                {

                    WallData next =

                        walls[wallIndex + 1];



                    barTop =

                        next.BottomZ

                        + lap;

                }



                // ----------------------------------------------------

                // ПОСЛЕДНЯЯ СТЕНА

                // ----------------------------------------------------



                else

                {

                    barTop =

                        GetFinalVerticalBarTop(

                            doc,

                            wall);

                }



                List<double> positions =
                    GetFinalMainVerticalPositions(
                        wall,
                        allWalls);



                double planeOffset =
                    wall.Thickness / 2.0
                    - ToFeet(
                        VerticalAxisOffsetMm);



                foreach (double position in positions)

                {

                    // На свободном торце СЖм-/СШм- обычные Ø10
                    // не должны дублировать специальные 6Ø12.
                    if (IsMainVerticalPositionBlockedByFreeEnd12(
                        doc, wall, allWalls, position))
                    {
                        continue;
                    }

                    // В Г-образном узле крайние Ø10 заменяются на 4Ø12:

                    // по две Ø12 на каждую из двух стен.

                    double cornerTolerance = ToFeet(2.0);

                    double endOffset = ToFeet(VerticalEndOffsetMm);



                    bool atStartCorner =

                        Math.Abs(position - endOffset) <= cornerTolerance

                        && IsWallEndInCorner(wall, WallEndSide.Start, allWalls);



                    bool atEndCorner =

                        Math.Abs(position - (wall.Length - endOffset)) <= cornerTolerance

                        && IsWallEndInCorner(wall, WallEndSide.End, allWalls);



                    if (atStartCorner || atEndCorner)

                    {

                        continue;

                    }



                    List<VerticalRange> ranges =

                        GetVerticalBarRanges(

                            wall,

                            position,

                            barBottom,

                            barTop);



                    foreach (VerticalRange range in ranges)

                    {

                        if (

                            range.Top - range.Bottom

                            < ToFeet(10.0))

                        {

                            continue;

                        }



                        XYZ axisPoint =

                            wall.StartPoint

                            + wall.Direction

                            * position;



                        count +=

                            CreateVerticalBarOnPlane(

                                doc,

                                wall,

                                barType,

                                axisPoint,

                                wall.Normal,

                                planeOffset,

                                range.Bottom,

                                range.Top,

                                +1);



                        count +=

                            CreateVerticalBarOnPlane(

                                doc,

                                wall,

                                barType,

                                axisPoint,

                                wall.Normal,

                                planeOffset,

                                range.Bottom,

                                range.Top,

                                -1);

                    }

                }

            }



            return count;

        }





        // ============================================================
        // КОРРЕКТИРОВКА Ø10 У Т-ОБРАЗНОГО УЗЛА
        // ============================================================

        // ============================================================
        // ЕДИНЫЕ ИТОГОВЫЕ ПОЗИЦИИ ВЕРТИКАЛЬНОЙ СЕТКИ
        // Ø10 + шпильки + вертикальные П-шки
        // ============================================================

        private static List<double> GetFinalMainVerticalPositions(
            WallData wall,
            List<WallData> allWalls)
        {
            List<double> positions =
                GetMainVerticalPositions(wall);

            double planeOffset =
                wall.Thickness / 2.0
                - ToFeet(VerticalAxisOffsetMm);

            return AdjustMainVerticalPositionsNearTJoints(
                wall,
                allWalls,
                positions,
                planeOffset,
                ToFeet(100.0));
        }


        private static List<double> AdjustMainVerticalPositionsNearTJoints(
            WallData wall,
            List<WallData> allWalls,
            List<double> sourcePositions,
            double planeOffset,
            double minDistance)
        {
            if (sourcePositions == null || sourcePositions.Count == 0)
                return sourcePositions;

            List<WallTJoint> joints = FindTJoints(allWalls)
                ?? new List<WallTJoint>();

            // Крест (+) использует ту же геометрию 4 узловых Ø12,
            // поэтому добавляем каждый крест как один геометрический
            // Т-узел для проверки расстояния обычных Ø10.
            List<WallCrossJoint> crosses = FindCrossJoints(allWalls);
            if (crosses != null)
            {
                foreach (WallCrossJoint cross in crosses)
                {
                    if (cross == null ||
                        cross.MainWall == null ||
                        cross.BranchA == null)
                    {
                        continue;
                    }

                    joints.Add(new WallTJoint
                    {
                        MainWall = cross.MainWall,
                        BranchWall = cross.BranchA,
                        BranchSide = cross.SideA,
                        JointPoint = cross.JointPoint,
                        BranchInward = GetWallInwardDirection(
                            cross.BranchA, cross.SideA),
                        AngleDegrees = 90.0
                    });
                }
            }

            if (joints.Count == 0)
                return sourcePositions;

            List<double> result = new List<double>(sourcePositions);
            double cover = ToFeet(50.0);
            double tolerance = ToFeet(0.5);

            foreach (WallTJoint joint in joints)
            {
                if (joint == null || joint.MainWall == null || joint.BranchWall == null)
                    continue;

                WallData main = joint.MainWall;
                WallData branch = joint.BranchWall;

                // Корректируем обычные Ø10 и на MAIN, и на BRANCH.
                // В зоне 4Ø12 обычная Ø10 не должна подходить
                // ближе 100 мм к узловому стержню.
                bool wallIsMain = main.Wall.Id == wall.Wall.Id;
                bool wallIsBranch = branch.Wall.Id == wall.Wall.Id;

                if (!wallIsMain && !wallIsBranch)
                    continue;

                if (main.Thickness <= 2.0 * cover || branch.Thickness <= 2.0 * cover)
                    continue;

                XYZ mainDirection =
                    new XYZ(main.Direction.X, main.Direction.Y, 0.0).Normalize();

                XYZ branchDirection =
                    new XYZ(branch.Direction.X, branch.Direction.Y, 0.0).Normalize();

                XYZ intersection =
                    GetInfiniteLineIntersection2D(
                        main.StartPoint,
                        mainDirection,
                        branch.StartPoint,
                        branchDirection);

                if (intersection == null)
                    continue;

                double alongMain = branch.Thickness / 2.0 - cover;
                double alongBranch = main.Thickness / 2.0 - cover;

                List<XYZ> tBars = new List<XYZ>
                {
                    intersection + mainDirection * alongMain + branchDirection * alongBranch,
                    intersection + mainDirection * alongMain - branchDirection * alongBranch,
                    intersection - mainDirection * alongMain + branchDirection * alongBranch,
                    intersection - mainDirection * alongMain - branchDirection * alongBranch
                };

                for (int i = 0; i < result.Count; i++)
                {
                    double original = result[i];

                    if (IsVerticalPositionSafe(
                        wall, original, planeOffset, tBars, minDistance, tolerance))
                    {
                        continue;
                    }

                    double best = original;
                    bool found = false;

                    // Ищем ближайшее допустимое положение с точностью 1 мм.
                    for (double shiftMm = 1.0; shiftMm <= 300.0; shiftMm += 1.0)
                    {
                        double shift = ToFeet(shiftMm);

                        double towardStart = original - shift;
                        if (IsVerticalCandidateSafe(
                            wall, result, i, towardStart, planeOffset,
                            tBars, minDistance, tolerance))
                        {
                            best = towardStart;
                            found = true;
                            break;
                        }

                        double towardEnd = original + shift;
                        if (IsVerticalCandidateSafe(
                            wall, result, i, towardEnd, planeOffset,
                            tBars, minDistance, tolerance))
                        {
                            best = towardEnd;
                            found = true;
                            break;
                        }
                    }

                    // Если допустимую позицию не нашли, исходный Ø10
                    // сохраняем — никогда не удаляем вертикальную арматуру.
                    if (found)
                        result[i] = best;
                }
            }

            result.Sort();
            return result;
        }


        private static bool IsVerticalCandidateSafe(
            WallData wall,
            List<double> positions,
            int currentIndex,
            double candidate,
            double planeOffset,
            List<XYZ> tBars,
            double minDistance,
            double tolerance)
        {
            double minPosition = ToFeet(VerticalEndOffsetMm);
            double maxPosition = wall.Length - ToFeet(VerticalEndOffsetMm);

            if (candidate < minPosition || candidate > maxPosition)
                return false;

            // Минимум 100 мм и до соседних Ø10.
            for (int j = 0; j < positions.Count; j++)
            {
                if (j == currentIndex)
                    continue;

                if (Math.Abs(candidate - positions[j]) < minDistance - tolerance)
                    return false;
            }

            return IsVerticalPositionSafe(
                wall, candidate, planeOffset, tBars, minDistance, tolerance);
        }


        private static bool IsVerticalPositionSafe(
            WallData wall,
            double position,
            double planeOffset,
            List<XYZ> tBars,
            double minDistance,
            double tolerance)
        {
            XYZ center =
                wall.StartPoint
                + wall.Direction * position;

            XYZ plus =
                center + wall.Normal * planeOffset;

            XYZ minus =
                center - wall.Normal * planeOffset;

            foreach (XYZ target in tBars)
            {
                if (PlanDistance(plus, target) < minDistance - tolerance)
                    return false;

                if (PlanDistance(minus, target) < minDistance - tolerance)
                    return false;
            }

            return true;
        }


        private static double PlanDistance(
            XYZ a,
            XYZ b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;

            return Math.Sqrt(dx * dx + dy * dy);
        }



        // ============================================================
        // ИСКЛЮЧЕНИЕ Ø10 В ЗОНЕ 6Ø12 СВОБОДНОГО ТОРЦА
        // ============================================================

        private static bool IsMainVerticalPositionBlockedByFreeEnd12(
            Document doc,
            WallData wall,
            List<WallData> allWalls,
            double position)
        {
            if (wall == null || wall.Wall == null)
                return false;

            if (doc == null)
                doc = wall.Wall.Document;

            if (!IsStiffnessOrShaftWall(doc, wall))
            {
                return false;
            }

            double tolerance = ToFeet(2.0);
            double[] offsetsMm = { 50.0, 200.0, 350.0 };

            if (IsWallEndFree(wall, WallEndSide.Start, allWalls))
            {
                foreach (double offsetMm in offsetsMm)
                {
                    if (Math.Abs(position - ToFeet(offsetMm)) <= tolerance)
                    {
                        return true;
                    }
                }
            }

            if (IsWallEndFree(wall, WallEndSide.End, allWalls))
            {
                foreach (double offsetMm in offsetsMm)
                {
                    double target = wall.Length - ToFeet(offsetMm);

                    if (Math.Abs(position - target) <= tolerance)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // ============================================================
        // 6Ø12 НА СВОБОДНОМ ТОРЦЕ СЖм- / СШм-
        // ============================================================
        // Только свободный торец специальных стен.
        // Г-, Т- и +-узлы этим методом не обрабатываются.
        // 3 позиции вдоль стены: 50 / 200 / 350 мм.
        // На каждой позиции два стержня по двум граням = 6 стержней.
        // ============================================================

        private static int CreateFreeEndVertical12Bars(
            Document doc,
            List<WallData> allWalls,
            RebarBarType barType,
            double lapMmFor10)
        {
            if (doc == null || allWalls == null || barType == null)
                return 0;

            int created = 0;
            double cover = ToFeet(VerticalAxisOffsetMm);
            double barDiameterMm = ToMm(GetBarDiameterFeet(barType));
            double lapMm = lapMmFor10 * barDiameterMm / 10.0;
            double[] offsetsMm = { 50.0, 200.0, 350.0 };

            foreach (WallData wall in allWalls)
            {
                if (wall == null || wall.Wall == null)
                    continue;

                if (!IsStiffnessOrShaftWall(doc, wall))
                    continue;

                bool startFree =
                    IsWallEndFree(wall, WallEndSide.Start, allWalls);

                bool endFree =
                    IsWallEndFree(wall, WallEndSide.End, allWalls);

                if (!startFree && !endFree)
                    continue;

                double planeOffset = wall.Thickness / 2.0 - cover;
                if (planeOffset <= ToFeet(1.0))
                    continue;

                double bottom = wall.BottomZ;
                double top = GetCornerVerticalBarTop(
                    doc, wall, allWalls, lapMm);

                if (top - bottom <= ToFeet(10.0))
                    continue;

                if (startFree)
                {
                    foreach (double offsetMm in offsetsMm)
                    {
                        double position = ToFeet(offsetMm);
                        if (position <= 0.0 || position >= wall.Length)
                            continue;

                        XYZ axisPoint =
                            wall.StartPoint + wall.Direction * position;

                        created += CreateVerticalBarOnPlane(
                            doc, wall, barType, axisPoint, wall.Normal,
                            planeOffset, bottom, top, +1);

                        created += CreateVerticalBarOnPlane(
                            doc, wall, barType, axisPoint, wall.Normal,
                            planeOffset, bottom, top, -1);
                    }
                }

                if (endFree)
                {
                    foreach (double offsetMm in offsetsMm)
                    {
                        double position =
                            wall.Length - ToFeet(offsetMm);

                        if (position <= 0.0 || position >= wall.Length)
                            continue;

                        XYZ axisPoint =
                            wall.StartPoint + wall.Direction * position;

                        created += CreateVerticalBarOnPlane(
                            doc, wall, barType, axisPoint, wall.Normal,
                            planeOffset, bottom, top, +1);

                        created += CreateVerticalBarOnPlane(
                            doc, wall, barType, axisPoint, wall.Normal,
                            planeOffset, bottom, top, -1);
                    }
                }
            }

            return created;
        }


        // ============================================================
        // КЛАССИФИКАЦИЯ СТЕНЫ: СЖм- / СШм-
        // Используется только для исключения обычных Ø10
        // в зоне специальных 6Ø12 свободного торца.
        // ============================================================

        private static bool IsStiffnessOrShaftWall(
            Document doc,
            WallData wall)
        {
            if (doc == null || wall == null || wall.Wall == null)
                return false;

            Element element = wall.Wall;
            Element type = doc.GetElement(element.GetTypeId());

            return ElementContainsClassification(element)
                || ElementContainsClassification(type);
        }

        private static bool ElementContainsClassification(Element element)
        {
            if (element == null)
                return false;

            string name = element.Name ?? string.Empty;

            if (name.IndexOf("СЖм-", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("СШм-", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            foreach (Parameter parameter in element.Parameters)
            {
                if (parameter == null || !parameter.HasValue)
                    continue;

                string value = null;

                try
                {
                    value = parameter.AsString();

                    if (string.IsNullOrWhiteSpace(value))
                        value = parameter.AsValueString();
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(value))
                    continue;

                if (value.IndexOf("СЖм-", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("СШм-", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("Стена шахты", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("Стены шахт", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }



        // ============================================================

        // ПОЗИЦИИ ОСНОВНОЙ ВЕРТИКАЛЬНОЙ АРМАТУРЫ

        // ============================================================



        private static List<double> GetMainVerticalPositions(

            WallData wall)

        {

            List<double> positions =

                new List<double>();



            double start =

                ToFeet(

                    VerticalEndOffsetMm);



            double spacing =

                ToFeet(

                    VerticalSpacingMm);



            double max =

                wall.Length

                - ToFeet(

                    VerticalEndOffsetMm);



            for (

                double x = start;

                x <= max + ToFeet(0.5);

                x += spacing)

            {

                positions.Add(x);

            }



            return positions;

        }





        // ============================================================

        // ВЕРТИКАЛЬНЫЕ ДИАПАЗОНЫ

        // С УЧЁТОМ ПРОЁМОВ

        // ============================================================



        private static List<VerticalRange> GetVerticalBarRanges(

            WallData wall,

            double position,

            double bottom,

            double top)

        {

            List<VerticalRange> ranges =

                new List<VerticalRange>();



            ranges.Add(

                new VerticalRange

                {

                    Bottom = bottom,

                    Top = top

                });



            if (wall.Openings == null)

            {

                return ranges;

            }



            foreach (OpeningData opening in wall.Openings)

            {

                if (

                    position <= opening.Left ||

                    position >= opening.Right)

                {

                    continue;

                }



                ranges =

                    SubtractVerticalRange(

                        ranges,

                        opening.BottomZ

                        - ToFeet(

                            OpeningVerticalGapMm),



                        opening.TopZ

                        + ToFeet(

                            OpeningVerticalGapMm));

            }



            return ranges;

        }





        // ============================================================

        // ВЫЧИТАНИЕ ПРОЁМА

        // ============================================================



        private static List<VerticalRange> SubtractVerticalRange(

            List<VerticalRange> source,

            double cutBottom,

            double cutTop)

        {

            List<VerticalRange> result =

                new List<VerticalRange>();



            foreach (VerticalRange range in source)

            {

                if (

                    cutTop <= range.Bottom ||

                    cutBottom >= range.Top)

                {

                    result.Add(range);



                    continue;

                }



                if (cutBottom > range.Bottom)

                {

                    result.Add(

                        new VerticalRange

                        {

                            Bottom =

                                range.Bottom,



                            Top =

                                Math.Min(

                                    cutBottom,

                                    range.Top)

                        });

                }



                if (cutTop < range.Top)

                {

                    result.Add(

                        new VerticalRange

                        {

                            Bottom =

                                Math.Max(

                                    cutTop,

                                    range.Bottom),



                            Top =

                                range.Top

                        });

                }

            }



            return result;

        }





        // ============================================================

        // ОДИН ВЕРТИКАЛЬНЫЙ СТЕРЖЕНЬ

        // ============================================================



        private static int CreateVerticalBarOnPlane(

            Document doc,

            WallData wall,

            RebarBarType barType,

            XYZ axisPoint,

            XYZ normal,

            double planeOffset,

            double bottomZ,

            double topZ,

            int side)

        {

            XYZ point =

                axisPoint

                + normal

                * planeOffset

                * side;



            XYZ p1 =

                new XYZ(

                    point.X,

                    point.Y,

                    bottomZ);



            XYZ p2 =

                new XYZ(

                    point.X,

                    point.Y,

                    topZ);



            return

                CreateSingleBar(

                    doc,

                    wall.Wall,

                    barType,

                    normal,

                    p1,

                    p2);

        }





        // ============================================================

        // ГОРИЗОНТАЛЬНАЯ АРМАТУРА

        // ============================================================



        private static int CreateHorizontalBars(

            Document doc,

            WallData wall,

            List<WallData> allWalls,

            RebarBarType barType,

            double horizontalAxisOffset)

        {

            int count = 0;



            List<double> levels =

                GetHorizontalLevels(

                    wall);



            double planeOffset =

                wall.Thickness / 2.0

                - horizontalAxisOffset;



            foreach (double z in levels)

            {

                List<HorizontalRange> ranges =

                    GetHorizontalRanges(

                        wall,

                        z);



                foreach (HorizontalRange range in ranges)

                {

                    // В кресте (+) примыкающие половины не создаём.
                    // Вместо них CreateCrossHorizontalBars создаёт
                    // два физических сплошных стержня через весь узел.
                    if (IsHorizontalRangeAtCrossEnd(
                        wall,
                        range,
                        allWalls))
                    {
                        continue;
                    }

                    if (

                        range.End - range.Start

                        < ToFeet(10.0))

                    {

                        continue;

                    }



                    count +=

                        CreateHorizontalBarOnPlane(

                            doc,

                            wall,

                            barType,

                            z,

                            range.Start,

                            range.End,

                            planeOffset,

                            +1);



                    count +=

                        CreateHorizontalBarOnPlane(

                            doc,

                            wall,

                            barType,

                            z,

                            range.Start,

                            range.End,

                            planeOffset,

                            -1);

                }

            }



            return count;

        }





        // ============================================================

        // УРОВНИ ГОРИЗОНТАЛЬНОЙ АРМАТУРЫ

        // ============================================================



        private static List<double> GetHorizontalLevels(

            WallData wall)

        {

            List<double> result =

                new List<double>();



            double z =

                wall.BottomZ

                + ToFeet(

                    VerticalEndOffsetMm);



            double max =

                wall.TopZ

                - ToFeet(

                    VerticalEndOffsetMm);



            double spacing =

                ToFeet(

                    HorizontalSpacingMm);



            while (

                z <= max

                + ToFeet(0.5))

            {

                result.Add(z);



                z += spacing;

            }



            return result;

        }





        // ============================================================

        // УЧАСТКИ ГОРИЗОНТАЛЬНОЙ АРМАТУРЫ

        // ============================================================



        private static List<HorizontalRange> GetHorizontalRanges(

            WallData wall,

            double z)

        {

            List<HorizontalRange> ranges =

                new List<HorizontalRange>();



            double startPosition =

                wall.StartHorizontalExtension > ToFeet(0.5)

                ? -wall.StartHorizontalExtension

                : ToFeet(HorizontalConcreteEdgeOffsetMm);



            double endPosition =

                wall.EndHorizontalExtension > ToFeet(0.5)

                ? wall.Length + wall.EndHorizontalExtension

                : wall.Length - ToFeet(HorizontalConcreteEdgeOffsetMm);



            ranges.Add(

                new HorizontalRange

                {

                    Start = startPosition,

                    End = endPosition

                });



            if (wall.Openings == null)

            {

                return ranges;

            }



            foreach (OpeningData opening in wall.Openings)

            {

                if (

                    z <= opening.BottomZ ||

                    z >= opening.TopZ)

                {

                    continue;

                }



                double cutStart =

                    opening.Left

                    - ToFeet(

                        HorizontalConcreteEdgeOffsetMm);



                double cutEnd =

                    opening.Right

                    + ToFeet(

                        HorizontalConcreteEdgeOffsetMm);



                ranges =

                    SubtractHorizontalRange(

                        ranges,

                        cutStart,

                        cutEnd);

            }



            return ranges;

        }





        // ============================================================

        // ВЫЧИТАНИЕ ПРОЁМА ИЗ ГОРИЗОНТАЛЬНОГО СТЕРЖНЯ

        // ============================================================



        private static List<HorizontalRange> SubtractHorizontalRange(

            List<HorizontalRange> source,

            double cutStart,

            double cutEnd)

        {

            List<HorizontalRange> result =

                new List<HorizontalRange>();



            foreach (HorizontalRange range in source)

            {

                if (

                    cutEnd <= range.Start ||

                    cutStart >= range.End)

                {

                    result.Add(range);



                    continue;

                }



                if (cutStart > range.Start)

                {

                    result.Add(

                        new HorizontalRange

                        {

                            Start =

                                range.Start,



                            End =

                                Math.Min(

                                    cutStart,

                                    range.End)

                        });

                }



                if (cutEnd < range.End)

                {

                    result.Add(

                        new HorizontalRange

                        {

                            Start =

                                Math.Max(

                                    cutEnd,

                                    range.Start),



                            End =

                                range.End

                        });

                }

            }



            return result;

        }





        // ============================================================

        // ОДИН ГОРИЗОНТАЛЬНЫЙ СТЕРЖЕНЬ

        // ============================================================



        private static int CreateHorizontalBarOnPlane(

            Document doc,

            WallData wall,

            RebarBarType barType,

            double z,

            double startPosition,

            double endPosition,

            double planeOffset,

            int side)

        {

            XYZ p1 =

                wall.StartPoint

                + wall.Direction

                * startPosition

                + wall.Normal

                * planeOffset

                * side;



            XYZ p2 =

                wall.StartPoint

                + wall.Direction

                * endPosition

                + wall.Normal

                * planeOffset

                * side;



            p1 =

                new XYZ(

                    p1.X,

                    p1.Y,

                    z);



            p2 =

                new XYZ(

                    p2.X,

                    p2.Y,

                    z);



            return

                CreateSingleBar(

                    doc,

                    wall.Wall,

                    barType,

                    XYZ.BasisZ,

                    p1,

                    p2);

        }





        // ============================================================
        // ДВА СПЛОШНЫХ ГОРИЗОНТАЛЬНЫХ Ø10 ЧЕРЕЗ КРЕСТ (+)
        // ============================================================

        private static int CreateCrossHorizontalBars(
            Document doc,
            List<WallData> allWalls,
            RebarBarType barType,
            double horizontalAxisOffset)
        {
            int created = 0;
            List<WallCrossJoint> crosses = FindCrossJoints(allWalls);
            double zTolerance = ToFeet(1.0);

            foreach (WallCrossJoint cross in crosses)
            {
                WallData a = cross.BranchA;
                WallData b = cross.BranchB;

                List<double> levelsA = GetHorizontalLevels(a);
                List<double> levelsB = GetHorizontalLevels(b);

                double planeA = a.Thickness / 2.0 - horizontalAxisOffset;
                double planeB = b.Thickness / 2.0 - horizontalAxisOffset;

                foreach (double z in levelsA)
                {
                    double zB = levelsB
                        .OrderBy(x => Math.Abs(x - z))
                        .FirstOrDefault();

                    if (levelsB.Count == 0 || Math.Abs(zB - z) > zTolerance)
                        continue;

                    HorizontalRange rangeA = GetCrossAdjacentRange(a, cross.SideA, z);
                    HorizontalRange rangeB = GetCrossAdjacentRange(b, cross.SideB, zB);

                    if (rangeA == null || rangeB == null)
                        continue;

                    XYZ outerAPlus = GetCrossOuterPoint(a, cross.SideA, rangeA, planeA, +1, z);
                    XYZ outerAMinus = GetCrossOuterPoint(a, cross.SideA, rangeA, planeA, -1, z);

                    XYZ outerBPlus = GetCrossOuterPoint(b, cross.SideB, rangeB, planeB, +1, z);
                    XYZ outerBMinus = GetCrossOuterPoint(b, cross.SideB, rangeB, planeB, -1, z);

                    XYZ jointAPlus = GetCrossJointPlanePoint(a, cross.SideA, planeA, +1, z);
                    XYZ jointAMinus = GetCrossJointPlanePoint(a, cross.SideA, planeA, -1, z);
                    XYZ jointBPlus = GetCrossJointPlanePoint(b, cross.SideB, planeB, +1, z);
                    XYZ jointBMinus = GetCrossJointPlanePoint(b, cross.SideB, planeB, -1, z);

                    bool plusToPlus =
                        GetPlanDistance(jointAPlus, jointBPlus) +
                        GetPlanDistance(jointAMinus, jointBMinus)
                        <=
                        GetPlanDistance(jointAPlus, jointBMinus) +
                        GetPlanDistance(jointAMinus, jointBPlus);

                    XYZ bForAPlus = plusToPlus ? outerBPlus : outerBMinus;
                    XYZ bForAMinus = plusToPlus ? outerBMinus : outerBPlus;

                    created += CreateSingleBar(
                        doc,
                        a.Wall,
                        barType,
                        XYZ.BasisZ,
                        outerAPlus,
                        bForAPlus);

                    created += CreateSingleBar(
                        doc,
                        a.Wall,
                        barType,
                        XYZ.BasisZ,
                        outerAMinus,
                        bForAMinus);
                }
            }

            return created;
        }

        private static HorizontalRange GetCrossAdjacentRange(
            WallData wall,
            WallEndSide side,
            double z)
        {
            List<HorizontalRange> ranges = GetHorizontalRanges(wall, z);
            double tol = ToFeet(1.0);

            if (side == WallEndSide.Start)
                return ranges
                    .Where(x => x.Start <= tol)
                    .OrderBy(x => x.Start)
                    .FirstOrDefault();

            return ranges
                .Where(x => x.End >= wall.Length - tol)
                .OrderByDescending(x => x.End)
                .FirstOrDefault();
        }

        private static XYZ GetCrossOuterPoint(
            WallData wall,
            WallEndSide crossSide,
            HorizontalRange range,
            double planeOffset,
            int side,
            double z)
        {
            double position =
                crossSide == WallEndSide.Start
                ? range.End
                : range.Start;

            XYZ p = wall.StartPoint
                + wall.Direction * position
                + wall.Normal * planeOffset * side;

            return new XYZ(p.X, p.Y, z);
        }

        private static XYZ GetCrossJointPlanePoint(
            WallData wall,
            WallEndSide crossSide,
            double planeOffset,
            int side,
            double z)
        {
            XYZ end = GetWallEndPoint(wall, crossSide)
                + wall.Normal * planeOffset * side;

            return new XYZ(end.X, end.Y, z);
        }

        // ============================================================

        // ДОПОЛНИТЕЛЬНАЯ ВЕРТИКАЛЬНАЯ

        // У ПРОЁМОВ

        // ============================================================



        private static int CreateOpeningExtraVerticalBars(

            Document doc,

            WallData wall,

            RebarBarType barType)

        {

            int count = 0;



            if (

                wall.Openings == null ||

                wall.Openings.Count == 0)

            {

                return count;

            }



            List<double> mainPositions =

                GetMainVerticalPositions(

                    wall);



            double planeOffset =

                wall.Thickness / 2.0

                - ToFeet(

                    VerticalAxisOffsetMm);



            foreach (OpeningData opening in wall.Openings)

            {

                double leftDistance =

                    GetNearestDistance(

                        mainPositions,

                        opening.Left);



                double rightDistance =

                    GetNearestDistance(

                        mainPositions,

                        opening.Right);



                List<double> extraPositions =

                    new List<double>();



                if (

                    ToMm(leftDistance)

                    > OpeningExtraThresholdMm)

                {

                    AddPositionIfUnique(

                        extraPositions,

                        opening.Left

                        - ToFeet(

                            OpeningVerticalOffsetMm));

                }



                if (

                    ToMm(rightDistance)

                    > OpeningExtraThresholdMm)

                {

                    AddPositionIfUnique(

                        extraPositions,

                        opening.Right

                        + ToFeet(

                            OpeningVerticalOffsetMm));

                }



                foreach (

                    double position

                    in extraPositions)

                {

                    if (

                        position <= 0 ||

                        position >= wall.Length)

                    {

                        continue;

                    }



                    XYZ axisPoint =

                        wall.StartPoint

                        + wall.Direction

                        * position;



                    double bottom =

                        wall.BottomZ;



                    // -----------------------------------------------

                    // ВАЖНО:

                    //

                    // Для доп. вертикальной тоже учитываем плиту.

                    // -----------------------------------------------



                    double top =

                        GetFinalVerticalBarTop(

                            doc,

                            wall);



                    List<VerticalRange> ranges =

                        GetVerticalBarRanges(

                            wall,

                            position,

                            bottom,

                            top);



                    foreach (

                        VerticalRange range

                        in ranges)

                    {

                        if (

                            range.Top - range.Bottom

                            < ToFeet(10.0))

                        {

                            continue;

                        }



                        count +=

                            CreateVerticalBarOnPlane(

                                doc,

                                wall,

                                barType,

                                axisPoint,

                                wall.Normal,

                                planeOffset,

                                range.Bottom,

                                range.Top,

                                +1);



                        count +=

                            CreateVerticalBarOnPlane(

                                doc,

                                wall,

                                barType,

                                axisPoint,

                                wall.Normal,

                                planeOffset,

                                range.Bottom,

                                range.Top,

                                -1);

                    }

                }

            }



            return count;

        }





        // ============================================================

        // БЛИЖАЙШАЯ ПОЗИЦИЯ

        // ============================================================



        private static double GetNearestDistance(

            List<double> positions,

            double target)

        {

            if (positions.Count == 0)

            {

                return double.MaxValue;

            }



            return

                positions.Min(

                    x =>

                        Math.Abs(

                            x - target));

        }





        // ============================================================

        // ДОБАВИТЬ ПОЗИЦИЮ БЕЗ ДУБЛЯ

        // ============================================================



        private static void AddPositionIfUnique(

            List<double> positions,

            double value)

        {

            double tolerance =

                ToFeet(1.0);



            foreach (

                double existing

                in positions)

            {

                if (

                    Math.Abs(

                        existing - value)

                    <= tolerance)

                {

                    return;

                }

            }



            positions.Add(value);

        }





        // ============================================================

        // СОЗДАНИЕ ОДНОГО ПРЯМОГО СТЕРЖНЯ

        // ============================================================



        private static int CreateSingleBar(

            Document doc,

            Element host,

            RebarBarType barType,

            XYZ normal,

            XYZ p1,

            XYZ p2)

        {

            if (

                p1.DistanceTo(p2)

                < ToFeet(1.0))

            {

                return 0;

            }



            List<Curve> curves =

                new List<Curve>();



            curves.Add(

                Line.CreateBound(

                    p1,

                    p2));



            Rebar rebar =

                Rebar.CreateFromCurves(

                    doc,

                    RebarStyle.Standard,

                    barType,

                    null,

                    null,

                    host,

                    normal,

                    curves,

                    RebarHookOrientation.Left,

                    RebarHookOrientation.Left,

                    true,

                    true);



            return

                rebar == null

                ? 0

                : 1;

        }





        // ============================================================

        // ПОИСК ТИПА АРМАТУРЫ СТЕНЫ

        // ============================================================



        private static RebarBarType FindWallBarType(

            Document doc,

            double diameterMm,

            string namePart)

        {

            List<RebarBarType> types =

                new FilteredElementCollector(doc)

                .OfClass(

                    typeof(RebarBarType))

                .Cast<RebarBarType>()

                .ToList();



            foreach (

                RebarBarType type

                in types)

            {

                double diameter =

                    ToMm(

                        GetBarDiameterFeet(

                            type));



                if (

                    Math.Abs(

                        diameter

                        - diameterMm)

                    > 0.5)

                {

                    continue;

                }



                if (

                    type.Name.IndexOf(

                        namePart,

                        StringComparison

                            .OrdinalIgnoreCase)

                    >= 0)

                {

                    return type;

                }

            }



            return null;

        }





        // ============================================================

        // ДИАМЕТР АРМАТУРЫ

        // REVIT 2023

        // ============================================================



        private static double GetBarDiameterFeet(

            RebarBarType barType)

        {

            Parameter parameter =

                barType.get_Parameter(

                    BuiltInParameter

                        .REBAR_BAR_DIAMETER);



            if (

                parameter == null ||

                !parameter.HasValue)

            {

                throw new Exception(

                    "Не удалось получить диаметр типа: "

                    + barType.Name);

            }



            return

                parameter.AsDouble();

        }

    }

}
