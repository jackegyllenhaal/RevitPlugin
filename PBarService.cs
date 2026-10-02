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



        // ПОИСК ТИПА П-ШКИ Ø10



        // ============================================================







        private static RebarBarType FindPBarType(Document doc)



        {



            RebarBarType barType =



                new FilteredElementCollector(doc)



                .OfClass(typeof(RebarBarType))



                .Cast<RebarBarType>()



                .FirstOrDefault(x =>



                {



                    double diameterMm =



                        ToMm(GetBarDiameterFeet(x));







                    bool diameterOk =



                        Math.Abs(diameterMm - 10.0) < 0.5;







                    bool detailsOk =



                        x.Name.IndexOf(



                            "детали",



                            StringComparison.OrdinalIgnoreCase) >= 0;







                    return diameterOk && detailsOk;



                });







            if (barType == null)



            {



                throw new Exception(



                    "Не найден тип арматуры: детали Ø10.");



            }







            return barType;



        }











        // ============================================================



        // ГОРИЗОНТАЛЬНЫЕ П-ШКИ



        //



        // Свободный край:



        //     П-шка создаётся.



        //



        // Край проёма:



        //     П-шка создаётся.



        //



        // Г-образный узел:



        //     физический торец стены пропускается;



        //     П-шка создаётся на НОВОМ конце продлённой Ø10.



        //



        // Ножки = 500 мм.



        // ============================================================







        private static int CreateTestPBars(



            Document doc,



            WallData wall,



            List<WallData> allWalls,



            double horizontalAxisOffset)



        {



            int created = 0;







            RebarBarType barType =



                FindPBarType(doc);







            double pBarDiameter =



                GetBarDiameterFeet(barType);







            double pBarRadius =



                pBarDiameter / 2.0;











            // ========================================================



            // ГОРИЗОНТАЛЬНАЯ Ø10



            // ========================================================







            double horizontalDiameter =



                ToFeet(10.0);







            double horizontalRadius =



                horizontalDiameter / 2.0;











            // ========================================================



            // РАЗНОС ПО ВЫСОТЕ



            //



            // Ø10/2 + Ø10/2 = 10 мм



            // ========================================================







            double verticalOffset =



                horizontalRadius +



                pBarRadius;











            // ========================================================



            // ШИРИНА П-ШКИ



            //



            // Горизонтальная арматура расположена



            // на horizontalAxisOffset от граней.



            // ========================================================







            double pBarWidth =



                wall.Thickness -



                2.0 * horizontalAxisOffset;







            if (pBarWidth <= ToFeet(20.0))



            {



                throw new Exception(



                    "Недостаточная ширина горизонтальной П-шки.");



            }











            // ========================================================



            // Г-ОБРАЗНЫЙ УЗЕЛ



            // ========================================================







            bool startIsCorner =



                IsWallEndInCorner(



                    wall,



                    WallEndSide.Start,



                    allWalls);







            bool endIsCorner =



                IsWallEndInCorner(



                    wall,



                    WallEndSide.End,



                    allWalls);

            // ========================================================
            // Т-ОБРАЗНЫЙ УЗЕЛ
            // ========================================================

            WallTJoint startTJoint =
                GetTJointForBranchEnd(
                    wall,
                    WallEndSide.Start,
                    allWalls);

            WallTJoint endTJoint =
                GetTJointForBranchEnd(
                    wall,
                    WallEndSide.End,
                    allWalls);

            bool startIsTJoint = startTJoint != null;
            bool endIsTJoint = endTJoint != null;












            // ========================================================



            // ПРОДЛЕНИЕ ГОРИЗОНТАЛЬНОЙ АРМАТУРЫ



            // ========================================================







            double extensionTolerance =



                ToFeet(0.5);







            bool startIsExtended =



                wall.StartHorizontalExtension >



                extensionTolerance;







            bool endIsExtended =



                wall.EndHorizontalExtension >



                extensionTolerance;











            // ========================================================



            // УРОВНИ ГОРИЗОНТАЛЬНОЙ АРМАТУРЫ



            // ========================================================







            List<double> levels =



                GetHorizontalLevels(wall);







            double edgeTolerance =



                ToFeet(5.0);











            foreach (double horizontalZ in levels)



            {



                // ====================================================



                // П-шка находится выше горизонтальной Ø10



                // на сумму радиусов.



                // ====================================================







                double pBarZ =



                    horizontalZ +



                    verticalOffset;











                // ====================================================



                // ФАКТИЧЕСКИЕ УЧАСТКИ ГОРИЗОНТАЛЬНОЙ Ø10



                // ====================================================







                List<HorizontalRange> ranges =



                    GetHorizontalRanges(



                        wall,



                        horizontalZ);











                // ====================================================



                // ОБЫЧНЫЕ П-ШКИ



                //



                // Здесь создаём:



                // - свободные края;



                // - края проёмов.



                //



                // Продлённые концы Г-узла создаются



                // ОТДЕЛЬНО ниже.



                // ====================================================







                foreach (HorizontalRange range in ranges)



                {



                    if (



                        range.End - range.Start <



                        ToFeet(10.0))



                    {



                        continue;



                    }











                    // =================================================



                    // ЛЕВАЯ ГРАНИЦА RANGE



                    // =================================================







                    bool rangeStartsAtPhysicalStart =



                        Math.Abs(



                            range.Start)



                        <= edgeTolerance;











                    bool rangeStartsAtExtendedStart =



                        startIsExtended



                        &&



                        range.Start <



                        -extensionTolerance;











                    // -------------------------------------------------



                    // Если это продлённый START,



                    // здесь не создаём.



                    //



                    // Создадим гарантированно ниже.



                    // -------------------------------------------------







                    bool skipLeftBecauseExtended =



                        rangeStartsAtExtendedStart;











                    // -------------------------------------------------



                    // Если это физический START стены



                    // и он входит в CORNER —



                    // старую П-шку не создаём.



                    // -------------------------------------------------







                    bool skipLeftBecauseCorner =



                        rangeStartsAtPhysicalStart



                        &&



                        startIsCorner;











                    if (



                        !skipLeftBecauseExtended &&



                        !skipLeftBecauseCorner)



                    {



                        Rebar leftPBar =



                            CreateHorizontalEndPBar(



                                doc,



                                wall,



                                barType,



                                pBarWidth,



                                pBarZ,



                                range.Start,



                                +1);







                        if (leftPBar != null)



                        {



                            created++;



                        }



                    }











                    // =================================================



                    // ПРАВАЯ ГРАНИЦА RANGE



                    // =================================================







                    bool rangeEndsAtPhysicalEnd =



                        Math.Abs(



                            range.End -



                            wall.Length)



                        <= edgeTolerance;











                    bool rangeEndsAtExtendedEnd =



                        endIsExtended



                        &&



                        range.End >



                        wall.Length +



                        extensionTolerance;











                    bool skipRightBecauseExtended =



                        rangeEndsAtExtendedEnd;











                    bool skipRightBecauseCorner =



                        rangeEndsAtPhysicalEnd



                        &&



                        endIsCorner;











                    if (



                        !skipRightBecauseExtended &&



                        !skipRightBecauseCorner)



                    {



                        Rebar rightPBar =



                            CreateHorizontalEndPBar(



                                doc,



                                wall,



                                barType,



                                pBarWidth,



                                pBarZ,



                                range.End,



                                -1);







                        if (rightPBar != null)



                        {



                            created++;



                        }



                    }



                }











                // ====================================================



                // Г-УЗЕЛ — START



                //



                // Горизонтальная Ø10 продлена ЗА начало стены.



                //



                // Новый конец:



                //



                //     -StartHorizontalExtension



                //



                // П-шка ставится непосредственно на новый конец.



                //



                // Ножки направлены обратно внутрь исходной стены:



                //



                //     +wall.Direction



                // ====================================================







                if (



                    startIsCorner &&



                    startIsExtended)



                {



                    double cornerStartPosition =



                        -wall.StartHorizontalExtension;











                    Rebar cornerStartPBar =



                        CreateHorizontalEndPBar(



                            doc,



                            wall,



                            barType,



                            pBarWidth,



                            pBarZ,



                            cornerStartPosition,



                            +1);











                    if (cornerStartPBar != null)



                    {



                        created++;



                    }



                }











                // ====================================================



                // Г-УЗЕЛ — END



                //



                // Горизонтальная Ø10 продлена ЗА конец стены.



                //



                // Новый конец:



                //



                //     wall.Length + EndHorizontalExtension



                //



                // П-шка ставится непосредственно на новый конец.



                //



                // Ножки направлены обратно внутрь исходной стены:



                //



                //     -wall.Direction



                // ====================================================







                if (



                    endIsCorner &&



                    endIsExtended)



                {



                    double cornerEndPosition =



                        wall.Length +



                        wall.EndHorizontalExtension;











                    Rebar cornerEndPBar =



                        CreateHorizontalEndPBar(



                            doc,



                            wall,



                            barType,



                            pBarWidth,



                            pBarZ,



                            cornerEndPosition,



                            -1);











                    if (cornerEndPBar != null)



                    {



                        created++;



                    }



                }





                // ====================================================
                // Т-УЗЕЛ — START
                // ====================================================

                if (startIsTJoint && startIsExtended)
                {
                    double tPBarWidth =
                        startTJoint.MainWall.Thickness
                        - 2.0 * horizontalAxisOffset;

                    if (tPBarWidth > ToFeet(20.0))
                    {
                        double tStartPosition =
                            -wall.StartHorizontalExtension;

                        Rebar tStartPBar =
                            CreateHorizontalEndPBar(
                                doc,
                                wall,
                                barType,
                                tPBarWidth,
                                pBarZ,
                                tStartPosition,
                                +1);

                        if (tStartPBar != null)
                        {
                            created++;
                        }
                    }
                }

                // ====================================================
                // Т-УЗЕЛ — END
                // ====================================================

                if (endIsTJoint && endIsExtended)
                {
                    double tPBarWidth =
                        endTJoint.MainWall.Thickness
                        - 2.0 * horizontalAxisOffset;

                    if (tPBarWidth > ToFeet(20.0))
                    {
                        double tEndPosition =
                            wall.Length
                            + wall.EndHorizontalExtension;

                        Rebar tEndPBar =
                            CreateHorizontalEndPBar(
                                doc,
                                wall,
                                barType,
                                tPBarWidth,
                                pBarZ,
                                tEndPosition,
                                -1);

                        if (tEndPBar != null)
                        {
                            created++;
                        }
                    }
                }

            }











            // ====================================================
            // ВЕРТИКАЛЬНАЯ П-ШКА — ТОЛЬКО T-JOINT
            // Г-узлы и крестообразные (+) узлы сюда не попадают.
            // ====================================================

            if (startIsTJoint)
            {
                created += CreateTJointVerticalPBar(
                    doc, wall, startTJoint, WallEndSide.Start);
            }

            if (endIsTJoint)
            {
                created += CreateTJointVerticalPBar(
                    doc, wall, endTJoint, WallEndSide.End);
            }

            return created;
        }


        private static int CreateTJointVerticalPBar(
            Document doc,
            WallData wall,
            WallTJoint joint,
            WallEndSide side)
        {
            if (doc == null ||
                wall == null ||
                wall.Wall == null ||
                joint == null ||
                joint.BranchWall == null ||
                joint.MainWall == null)
            {
                return 0;
            }

            // ========================================================
            // ЖЁСТКАЯ ИЗОЛЯЦИЯ:
            // метод вызывается только от BranchWall конкретного TJoint.
            // Г-узел, + узел и обычная стена сюда не попадают.
            // ========================================================

            if (joint.BranchWall.Wall.Id != wall.Wall.Id ||
                joint.BranchSide != side)
            {
                return 0;
            }

            WallData mainWall = joint.MainWall;
            WallData branchWall = joint.BranchWall;

            RebarBarType barType =
                FindPBarType(doc);

            double pBarRadius =
                GetBarDiameterFeet(barType) / 2.0;

            double verticalAxisOffset =
                ToFeet(VerticalAxisOffsetMm);

            double pBarWidth =
                mainWall.Thickness
                - 2.0 * verticalAxisOffset;

            if (pBarWidth <= ToFeet(20.0))
            {
                return 0;
            }

            // ========================================================
            // Z текущего этажа.
            // Это вертикальные П-шки T-узла, ножки идут вниз.
            // ========================================================

            double concreteTop =
                GetTopPBarConcreteTop(
                    doc,
                    branchWall);

            double crossZ =
                concreteTop
                - ToFeet(20.0)
                - pBarRadius;

            // ========================================================
            // ФАКТИЧЕСКАЯ ТОЧКА ПЕРЕСЕЧЕНИЯ ОСЕЙ MAIN / BRANCH
            // ========================================================

            XYZ intersection;

            if (!TryGetPlanLineIntersection(
                    mainWall.StartPoint,
                    mainWall.Direction,
                    branchWall.StartPoint,
                    branchWall.Direction,
                    out intersection))
            {
                intersection = joint.JointPoint;
            }

            XYZ mainDirection =
                mainWall.Direction.Normalize();

            XYZ fromMainStart =
                new XYZ(
                    intersection.X - mainWall.StartPoint.X,
                    intersection.Y - mainWall.StartPoint.Y,
                    0.0);

            double intersectionPosition =
                fromMainStart.DotProduct(mainDirection);

            // ========================================================
            // ДВЕ П-ШКИ:
            //
            // по двум сторонам примыкающей BranchWall.
            // Ось П-шки находится на 50 мм от грани BranchWall,
            // как и узловые вертикальные стержни.
            //
            //        MAIN
            //          |
            //      ----+----  P1
            // BRANCH ->|
            //      ----+----  P2
            //          |
            // ========================================================

            double sidePosition =
                branchWall.Thickness / 2.0
                - ToFeet(VerticalAxisOffsetMm);

            if (sidePosition < ToFeet(5.0))
            {
                return 0;
            }

            double position1 =
                intersectionPosition - sidePosition;

            double position2 =
                intersectionPosition + sidePosition;

            int created = 0;

            if (position1 > ToFeet(5.0) &&
                position1 < mainWall.Length - ToFeet(5.0))
            {
                Rebar pBar1 =
                    CreateVerticalEdgePBar(
                        doc,
                        mainWall,
                        barType,
                        pBarWidth,
                        crossZ,
                        position1,
                        -1);

                if (pBar1 != null)
                {
                    created++;
                }
            }

            if (position2 > ToFeet(5.0) &&
                position2 < mainWall.Length - ToFeet(5.0))
            {
                Rebar pBar2 =
                    CreateVerticalEdgePBar(
                        doc,
                        mainWall,
                        barType,
                        pBarWidth,
                        crossZ,
                        position2,
                        -1);

                if (pBar2 != null)
                {
                    created++;
                }
            }

            return created;
        }


        // ============================================================
        // ПЕРЕСЕЧЕНИЕ ДВУХ ОСЕЙ В ПЛАНЕ
        // Только для T-specific П-шек.
        // ============================================================

        private static bool TryGetPlanLineIntersection(
            XYZ p1,
            XYZ d1,
            XYZ p2,
            XYZ d2,
            out XYZ intersection)
        {
            intersection = null;

            double cross =
                d1.X * d2.Y
                - d1.Y * d2.X;

            if (Math.Abs(cross) < 1e-9)
            {
                return false;
            }

            double dx =
                p2.X - p1.X;

            double dy =
                p2.Y - p1.Y;

            double t =
                (dx * d2.Y - dy * d2.X)
                / cross;

            intersection =
                new XYZ(
                    p1.X + d1.X * t,
                    p1.Y + d1.Y * t,
                    p1.Z);

            return true;
        }


        // ============================================================



        // ОДНА ГОРИЗОНТАЛЬНАЯ П-ШКА



        // ============================================================







        private static Rebar CreateHorizontalEndPBar(



            Document doc,



            WallData wall,



            RebarBarType barType,



            double pBarWidth,



            double z,



            double positionAlongWall,



            int inwardSign)



        {



            double legLength =



                ToFeet(500.0);











            XYZ direction =



                wall.Direction.Normalize();











            XYZ normal =



                wall.Normal.Normalize();











            // ========================================================



            // НОЖКИ ВСЕГДА СМОТРЯТ ВНУТРЬ СТЕНЫ



            // ========================================================







            XYZ inwardDirection =



                inwardSign > 0



                ? direction



                : -direction;











            // ========================================================



            // ЦЕНТР ПЕРЕМЫЧКИ



            // ========================================================







            // ========================================================

            // ОТСТУП КОРОТКОЙ СТОРОНЫ П-ШКИ ОТ КРАЯ

            //

            // START -> 20 мм внутрь по +Direction

            // END   -> 20 мм внутрь по -Direction

            // ========================================================



            double edgeOffset =

                ToFeet(5.0);



            double adjustedPosition =

                positionAlongWall

                + inwardSign * edgeOffset;



            // ========================================================

            // ЦЕНТР ПЕРЕМЫЧКИ

            // ========================================================



            XYZ crossCenter =

                new XYZ(

                    wall.StartPoint.X,

                    wall.StartPoint.Y,

                    z)

                + direction * adjustedPosition;











            // ========================================================



            // ПЕРЕМЫЧКА ЧЕРЕЗ ТОЛЩИНУ СТЕНЫ



            // ========================================================







            XYZ crossA =



                crossCenter



                + normal * (pBarWidth / 2.0);











            XYZ crossB =



                crossCenter



                - normal * (pBarWidth / 2.0);











            // ========================================================



            // НОЖКИ 500 ММ



            // ========================================================







            XYZ legAEnd =



                crossA



                + inwardDirection * legLength;











            XYZ legBEnd =



                crossB



                + inwardDirection * legLength;











            List<Curve> curves =



                new List<Curve>



                {



                    Line.CreateBound(



                        legAEnd,



                        crossA),







                    Line.CreateBound(



                        crossA,



                        crossB),







                    Line.CreateBound(



                        crossB,



                        legBEnd)



                };











            Rebar pBar =



                Rebar.CreateFromCurves(



                    doc,



                    RebarStyle.Standard,



                    barType,



                    null,



                    null,



                    wall.Wall,



                    XYZ.BasisZ,



                    curves,



                    RebarHookOrientation.Left,



                    RebarHookOrientation.Left,



                    true,



                    true);











            if (pBar == null)



            {



                throw new Exception(



                    "Не удалось создать горизонтальную П-шку.");



            }











            return pBar;



        }











        // ============================================================



        // ВЕРХНИЕ ВЕРТИКАЛЬНЫЕ П-ШКИ



        //



        // Ширина:



        // по вертикальной сетке — 50 мм от граней.



        //



        // Сдвиг:



        // 10 мм от вертикального стержня.



        //



        // Ножки:



        // 500 мм вниз.



        // ============================================================







        // ============================================================
        // ПОЗИЦИИ ВЕРХНИХ П-ШЕК
        //
        // На свободном торце обычные Ø10 в первых 350 мм заменены
        // специальными 6Ø12. Поэтому верхние П-шки должны повторять
        // только крайние позиции Ø12: 50 / 350 мм.
        //
        // На несвободных торцах сохраняется прежняя сетка.
        // ============================================================

        private static List<double> GetTopVerticalPBarPositions(
            Document doc,
            WallData wall,
            List<WallData> allWalls)
        {
            List<double> source =
                GetFinalMainVerticalPositions(
                    wall,
                    allWalls);

            List<double> result =
                new List<double>();

            // Сначала оставляем обычные позиции вне зоны 6Ø12.
            foreach (double position in source)
            {
                if (!IsMainVerticalPositionBlockedByFreeEnd12(
                        doc,
                        wall,
                        allWalls,
                        position))
                {
                    result.Add(position);
                }
            }

            // На свободном торце верхние П-шки нужны только
            // на крайних рядах специальных Ø12:
            // 50 мм и 350 мм.
            // На среднем ряду Ø12 (200 мм) П-шку не создаём.
            bool hasSpecialFreeEnd12 =
                IsStiffnessOrShaftWall(doc, wall);

            double[] offsetsMm =
            {
                50.0,
                350.0
            };

            // Затем добавляем две крайние специальные позиции Ø12
            // на каждом действительно свободном торце.
            if (hasSpecialFreeEnd12 &&
                IsWallEndFree(
                    wall,
                    WallEndSide.Start,
                    allWalls))
            {
                foreach (double offsetMm in offsetsMm)
                {
                    double position =
                        ToFeet(offsetMm);

                    if (position > 0.0 &&
                        position < wall.Length)
                    {
                        AddUniquePosition(
                            result,
                            position);
                    }
                }
            }

            if (hasSpecialFreeEnd12 &&
                IsWallEndFree(
                    wall,
                    WallEndSide.End,
                    allWalls))
            {
                foreach (double offsetMm in offsetsMm)
                {
                    double position =
                        wall.Length
                        - ToFeet(offsetMm);

                    if (position > 0.0 &&
                        position < wall.Length)
                    {
                        AddUniquePosition(
                            result,
                            position);
                    }
                }
            }

            result.Sort();
            return result;
        }


        private static void AddUniquePosition(
            List<double> positions,
            double position)
        {
            double tolerance =
                ToFeet(1.0);

            foreach (double existing in positions)
            {
                if (Math.Abs(existing - position) <= tolerance)
                    return;
            }

            positions.Add(position);
        }


        private static int CreateTopVerticalPBars(



            Document doc,



            WallData wall,



            List<WallData> allWalls,



            double horizontalAxisOffset)



        {



            int created = 0;











            RebarBarType barType =



                FindPBarType(doc);











            double pBarDiameter =



                GetBarDiameterFeet(barType);











            double pBarRadius =



                pBarDiameter / 2.0;











            // ========================================================



            // ОСЬ ВЕРТИКАЛЬНОЙ АРМАТУРЫ



            //



            // 50 мм от поверхности бетона



            // ========================================================







            double verticalAxisOffset =



                ToFeet(



                    VerticalAxisOffsetMm);











            // ========================================================



            // ШИРИНА ВЕРТИКАЛЬНОЙ П-ШКИ



            //



            // t200 -> 100 мм



            // t250 -> 150 мм



            // t300 -> 200 мм



            // ========================================================







            double pBarWidth =



                wall.Thickness -



                2.0 * verticalAxisOffset;











            if (pBarWidth <= ToFeet(20.0))



            {



                throw new Exception(



                    "Недостаточная ширина вертикальной П-шки.");



            }











            // ========================================================



            // ФАКТИЧЕСКИЙ ВЕРХ БЕТОНА



            // ========================================================







            double concreteTop =



                GetTopPBarConcreteTop(



                    doc,



                    wall);











            // ========================================================



            // ПЕРЕМЫЧКА П-ШКИ



            //



            // 20 мм от верхней поверхности



            // ========================================================







            double crossZ =



                concreteTop



                - ToFeet(20.0)



                - pBarRadius;











            // ========================================================



            // ОСНОВНЫЕ ВЕРТИКАЛЬНЫЕ ПОЗИЦИИ



            // ========================================================







            List<double> positions =
                GetTopVerticalPBarPositions(
                    doc,
                    wall,
                    allWalls);











            foreach (double position in positions)



            {



                if (



                    IsPositionInsideOpening(



                        wall,



                        position,



                        crossZ))



                {



                    continue;



                }











                Rebar pBar =



                    CreateVerticalEdgePBar(



                        doc,



                        wall,



                        barType,



                        pBarWidth,



                        crossZ,



                        position,



                        -1);











                if (pBar != null)



                {



                    created++;



                }



            }











            // ========================================================



            // ДОПОЛНИТЕЛЬНЫЕ ВЕРТИКАЛЬНЫЕ СТЕРЖНИ



            // У КРАЁВ ПРОЁМОВ



            // ========================================================







            if (wall.Openings != null)



            {



                double firstPosition =



                    ToFeet(



                        VerticalEndOffsetMm);











                double spacing =



                    ToFeet(



                        VerticalSpacingMm);











                foreach (



                    OpeningData opening



                    in wall.Openings)



                {



                    double leftPosition =



                        opening.Left



                        - ToFeet(



                            OpeningVerticalOffsetMm);











                    double rightPosition =



                        opening.Right



                        + ToFeet(



                            OpeningVerticalOffsetMm);











                    // =================================================



                    // ЛЕВАЯ ДОПОЛНИТЕЛЬНАЯ



                    // =================================================







                    if (



                        leftPosition > 0 &&



                        leftPosition < wall.Length)



                    {



                        double nearest =



                            GetNearestMainVerticalDistance(



                                leftPosition,



                                firstPosition,



                                spacing,



                                wall.Length);











                        if (



                            nearest >



                            ToFeet(



                                OpeningExtraThresholdMm))



                        {



                            Rebar pBar =



                                CreateVerticalEdgePBar(



                                    doc,



                                    wall,



                                    barType,



                                    pBarWidth,



                                    crossZ,



                                    leftPosition,



                                    -1);











                            if (pBar != null)



                            {



                                created++;



                            }



                        }



                    }











                    // =================================================



                    // ПРАВАЯ ДОПОЛНИТЕЛЬНАЯ



                    // =================================================







                    if (



                        rightPosition > 0 &&



                        rightPosition < wall.Length)



                    {



                        double nearest =



                            GetNearestMainVerticalDistance(



                                rightPosition,



                                firstPosition,



                                spacing,



                                wall.Length);











                        if (



                            nearest >



                            ToFeet(



                                OpeningExtraThresholdMm))



                        {



                            Rebar pBar =



                                CreateVerticalEdgePBar(



                                    doc,



                                    wall,



                                    barType,



                                    pBarWidth,



                                    crossZ,



                                    rightPosition,



                                    -1);











                            if (pBar != null)



                            {



                                created++;



                            }



                        }



                    }



                }



            }











            return created;



        }











        // ============================================================



        // ВЕРТИКАЛЬНЫЕ П-ШКИ НАД ПРОЁМАМИ



        //



        // Ширина — по вертикальной сетке.



        // Сдвиг — 10 мм.



        // Ножки — 500 мм вверх.



        // ============================================================







        private static int CreateOpeningTopPBars(



            Document doc,



            WallData wall,



            double horizontalAxisOffset)



        {



            int created = 0;











            if (



                wall.Openings == null ||



                wall.Openings.Count == 0)



            {



                return 0;



            }











            RebarBarType barType =



                FindPBarType(doc);











            double pBarDiameter =



                GetBarDiameterFeet(



                    barType);











            double pBarRadius =



                pBarDiameter / 2.0;











            // ========================================================



            // ВЕРТИКАЛЬНАЯ СЕТКА = 50 ММ ОТ ГРАНИ



            // ========================================================







            double verticalAxisOffset =



                ToFeet(



                    VerticalAxisOffsetMm);











            // ========================================================



            // ШИРИНА ВЕРТИКАЛЬНОЙ П-ШКИ



            // ========================================================







            double pBarWidth =



                wall.Thickness -



                2.0 * verticalAxisOffset;











            if (pBarWidth <= ToFeet(20.0))



            {



                throw new Exception(



                    "Недостаточная ширина П-шки над проёмом.");



            }











            // ========================================================



            // ОСНОВНЫЕ ВЕРТИКАЛЬНЫЕ ПОЗИЦИИ



            // ========================================================







            List<double> verticalPositions =



                GetMainVerticalPositions(



                    wall);











            foreach (



                OpeningData opening



                in wall.Openings)



            {



                // ====================================================



                // ПРОЁМ ДО ВЕРХА СТЕНЫ



                // ====================================================







                if (



                    opening.TopZ >=



                    wall.TopZ - ToFeet(50.0))



                {



                    continue;



                }











                // ====================================================



                // 20 ММ ОТ ВЕРХНЕЙ ГРАНИ ПРОЁМА



                // ====================================================







                double crossZ =



                    opening.TopZ



                    + ToFeet(20.0)



                    + pBarRadius;











                foreach (



                    double position



                    in verticalPositions)



                {



                    // ================================================



                    // ТОЛЬКО ПОЗИЦИИ НАД ПРОЁМОМ



                    // ================================================







                    if (



                        position <= opening.Left ||



                        position >= opening.Right)



                    {



                        continue;



                    }











                    Rebar pBar =



                        CreateVerticalEdgePBar(



                            doc,



                            wall,



                            barType,



                            pBarWidth,



                            crossZ,



                            position,



                            +1);











                    if (pBar != null)



                    {



                        created++;



                    }



                }



            }











            return created;



        }











        // ============================================================



        // СОЗДАНИЕ ОДНОЙ ВЕРТИКАЛЬНОЙ П-ШКИ



        //



        // legDirectionSign:



        //



        // +1 -> ножки вверх



        // -1 -> ножки вниз



        //



        // Ширина:



        // ножки находятся на уровне вертикальных сеток



        // по толщине стены.



        //



        // Сдвиг:



        // Ø10/2 + Ø10/2 = 10 мм.



        // ============================================================







        private static Rebar CreateVerticalEdgePBar(



            Document doc,



            WallData wall,



            RebarBarType barType,



            double pBarWidth,



            double crossZ,



            double positionAlongWall,



            int legDirectionSign)



        {



            // ========================================================



            // ДЛИНА НОЖЕК



            // ========================================================







            double legLength =



                ToFeet(500.0);











            XYZ direction =



                wall.Direction.Normalize();











            XYZ normal =



                wall.Normal.Normalize();











            // ========================================================



            // ДИАМЕТР ВЕРТИКАЛЬНОЙ АРМАТУРЫ



            // ========================================================







            double verticalBarDiameter =



                ToFeet(10.0);











            double verticalBarRadius =



                verticalBarDiameter / 2.0;











            // ========================================================



            // ДИАМЕТР П-ШКИ



            // ========================================================







            double pBarDiameter =



                GetBarDiameterFeet(



                    barType);











            double pBarRadius =



                pBarDiameter / 2.0;











            // ========================================================



            // СДВИГ МЕЖДУ ОСЯМИ



            //



            // Ø10:



            // 5 + 5 = 10 мм



            // ========================================================







            double sideOffset =



                verticalBarRadius



                + pBarRadius;











            // ========================================================



            // РАССТОЯНИЕ ДО START / END



            // ========================================================







            double distanceToStart =



                positionAlongWall;











            double distanceToEnd =



                wall.Length



                - positionAlongWall;











            // ========================================================



            // ЗОНА КРАЙНЕГО ВЕРТИКАЛЬНОГО СТЕРЖНЯ



            // ========================================================







            double edgeZone =



                ToFeet(100.0);











            double pBarPosition;











            // ========================================================



            // START



            //



            // Внутрь стены = +Direction



            // ========================================================







            if (distanceToStart <= edgeZone)



            {



                pBarPosition =



                    positionAlongWall



                    + sideOffset;



            }











            // ========================================================



            // END



            //



            // Внутрь стены = -Direction



            // ========================================================







            else if (distanceToEnd <= edgeZone)



            {



                pBarPosition =



                    positionAlongWall



                    - sideOffset;



            }











            // ========================================================



            // ВНУТРЕННЯЯ ПОЗИЦИЯ



            //



            // В том числе над проёмом.



            // ========================================================







            else



            {



                pBarPosition =



                    positionAlongWall



                    + sideOffset;



            }











            // ========================================================



            // ЗАЩИТА ОТ ВЫХОДА ЗА ПРЕДЕЛЫ СТЕНЫ



            // ========================================================







            double minimumPosition =



                ToFeet(5.0);











            double maximumPosition =



                wall.Length



                - ToFeet(5.0);











            if (pBarPosition < minimumPosition)



            {



                pBarPosition =



                    positionAlongWall



                    + sideOffset;



            }











            if (pBarPosition > maximumPosition)



            {



                pBarPosition =



                    positionAlongWall



                    - sideOffset;



            }











            // ========================================================



            // ЦЕНТР ПЕРЕМЫЧКИ



            // ========================================================







            XYZ crossCenter =



                new XYZ(



                    wall.StartPoint.X,



                    wall.StartPoint.Y,



                    crossZ)



                + direction * pBarPosition;











            // ========================================================



            // ШИРИНА ПО ТОЛЩИНЕ



            // ========================================================







            XYZ crossA =



                crossCenter



                + normal * (pBarWidth / 2.0);











            XYZ crossB =



                crossCenter



                - normal * (pBarWidth / 2.0);











            // ========================================================



            // НАПРАВЛЕНИЕ НОЖЕК



            // ========================================================







            XYZ legDirection =



                legDirectionSign > 0



                ? XYZ.BasisZ



                : -XYZ.BasisZ;











            // ========================================================



            // КОНЦЫ НОЖЕК



            // ========================================================







            XYZ legAEnd =



                crossA



                + legDirection * legLength;











            XYZ legBEnd =



                crossB



                + legDirection * legLength;











            // ========================================================



            // ГЕОМЕТРИЯ П-ШКИ



            // ========================================================







            List<Curve> curves =



                new List<Curve>



                {



                    Line.CreateBound(



                        legAEnd,



                        crossA),







                    Line.CreateBound(



                        crossA,



                        crossB),







                    Line.CreateBound(



                        crossB,



                        legBEnd)



                };











            // ========================================================



            // ПЛОСКОСТЬ ВЕРТИКАЛЬНОЙ П-ШКИ



            // ========================================================







            XYZ rebarPlaneNormal =



                direction;











            // ========================================================



            // СОЗДАНИЕ



            // ========================================================







            Rebar pBar =



                Rebar.CreateFromCurves(



                    doc,



                    RebarStyle.Standard,



                    barType,



                    null,



                    null,



                    wall.Wall,



                    rebarPlaneNormal,



                    curves,



                    RebarHookOrientation.Left,



                    RebarHookOrientation.Left,



                    true,



                    true);











            if (pBar == null)



            {



                throw new Exception(



                    "Не удалось создать вертикальную П-шку.");



            }











            return pBar;



        }











        // ============================================================



        // ПОПАДАЕТ ЛИ ПОЗИЦИЯ В ПРОЁМ



        // ============================================================







        private static bool IsPositionInsideOpening(



            WallData wall,



            double position,



            double z)



        {



            if (



                wall.Openings == null ||



                wall.Openings.Count == 0)



            {



                return false;



            }











            double tolerance =



                ToFeet(1.0);











            foreach (



                OpeningData opening



                in wall.Openings)



            {



                bool insideX =



                    position >



                    opening.Left + tolerance



                    &&



                    position <



                    opening.Right - tolerance;











                bool insideZ =



                    z >



                    opening.BottomZ + tolerance



                    &&



                    z <



                    opening.TopZ - tolerance;











                if (insideX && insideZ)



                {



                    return true;



                }



            }











            return false;



        }











        // ============================================================



        // РАССТОЯНИЕ ДО БЛИЖАЙШЕГО



        // ОСНОВНОГО ВЕРТИКАЛЬНОГО СТЕРЖНЯ



        // ============================================================







        private static double GetNearestMainVerticalDistance(



            double position,



            double firstPosition,



            double spacing,



            double wallLength)



        {



            double minDistance =



                double.MaxValue;











            for (



                double x = firstPosition;



                x <=



                    wallLength



                    - firstPosition



                    + ToFeet(1.0);



                x += spacing)



            {



                double distance =



                    Math.Abs(



                        position - x);











                if (distance < minDistance)



                {



                    minDistance =



                        distance;



                }



            }











            return minDistance;



        }



    }



}