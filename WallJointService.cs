using Autodesk.Revit.DB;



using System;

using System.Collections.Generic;

using System.Linq;

using System.Text;



namespace RevitPlugin

{

    public partial class HelloRevit

    {

        // ============================================================

        // КАКОЙ КОНЕЦ СТЕНЫ УЧАСТВУЕТ В УЗЛЕ

        // ============================================================



        private enum WallEndSide

        {

            Start,

            End

        }





        // ============================================================

        // ДАННЫЕ Г-ОБРАЗНОГО УЗЛА

        // ============================================================



        private class WallCornerJoint

        {

            public WallData WallA;



            public WallData WallB;



            public XYZ JointPoint;



            public WallEndSide SideA;



            public WallEndSide SideB;



            public XYZ InwardA;



            public XYZ InwardB;



            public double AngleDegrees;

        }





        // ============================================================

        // ПОИСК ВСЕХ Г-ОБРАЗНЫХ УЗЛОВ

        // ============================================================



        private static List<WallCornerJoint> FindCornerJoints(

            List<WallData> walls)

        {

            List<WallCornerJoint> joints =

                new List<WallCornerJoint>();



            if (walls == null || walls.Count < 2)

            {

                return joints;

            }



            for (int i = 0; i < walls.Count; i++)

            {

                WallData wallA =

                    walls[i];



                for (int j = i + 1; j < walls.Count; j++)

                {

                    WallData wallB =

                        walls[j];



                    // ------------------------------------------------

                    // СТЕНЫ ДОЛЖНЫ БЫТЬ НА ОДНОМ ЭТАЖЕ

                    // ------------------------------------------------



                    if (!AreWallsOnSameLevel(

                        wallA,

                        wallB))

                    {

                        continue;

                    }



                    // ------------------------------------------------

                    // УГОЛ МЕЖДУ СТЕНАМИ

                    // ------------------------------------------------



                    double angleDegrees =

                        GetWallAngleDegrees(

                            wallA,

                            wallB);



                    if (

                        angleDegrees < 80.0 ||

                        angleDegrees > 100.0)

                    {

                        continue;

                    }



                    // ------------------------------------------------

                    // ДОПУСТИМОЕ РАССТОЯНИЕ МЕЖДУ ТОРЦАМИ

                    // ------------------------------------------------



                    double allowedDistance =

                        wallA.Thickness / 2.0

                        +

                        wallB.Thickness / 2.0

                        +

                        ToFeet(10.0);



                    // START A + START B



                    TryAddCornerJoint(

                        joints,

                        wallA,

                        WallEndSide.Start,

                        wallB,

                        WallEndSide.Start,

                        allowedDistance,

                        angleDegrees);



                    // START A + END B



                    TryAddCornerJoint(

                        joints,

                        wallA,

                        WallEndSide.Start,

                        wallB,

                        WallEndSide.End,

                        allowedDistance,

                        angleDegrees);



                    // END A + START B



                    TryAddCornerJoint(

                        joints,

                        wallA,

                        WallEndSide.End,

                        wallB,

                        WallEndSide.Start,

                        allowedDistance,

                        angleDegrees);



                    // END A + END B



                    TryAddCornerJoint(

                        joints,

                        wallA,

                        WallEndSide.End,

                        wallB,

                        WallEndSide.End,

                        allowedDistance,

                        angleDegrees);

                }

            }



            return RemoveDuplicateCornerJoints(

                joints);

        }





        // ============================================================

        // ПРОВЕРКА ОДНОЙ КОМБИНАЦИИ ТОРЦОВ

        // ============================================================



        private static void TryAddCornerJoint(

            List<WallCornerJoint> joints,

            WallData wallA,

            WallEndSide sideA,

            WallData wallB,

            WallEndSide sideB,

            double allowedDistance,

            double angleDegrees)

        {

            XYZ pointA =

                GetWallEndPoint(

                    wallA,

                    sideA);



            XYZ pointB =

                GetWallEndPoint(

                    wallB,

                    sideB);



            double distance =

                GetPlanDistance(

                    pointA,

                    pointB);



            if (distance > allowedDistance)

            {

                return;

            }



            XYZ jointPoint =

                new XYZ(

                    (pointA.X + pointB.X) / 2.0,

                    (pointA.Y + pointB.Y) / 2.0,

                    wallA.BottomZ);



            XYZ inwardA =

                GetWallInwardDirection(

                    wallA,

                    sideA);



            XYZ inwardB =

                GetWallInwardDirection(

                    wallB,

                    sideB);



            WallCornerJoint joint =

                new WallCornerJoint

                {

                    WallA = wallA,

                    WallB = wallB,

                    JointPoint = jointPoint,

                    SideA = sideA,

                    SideB = sideB,

                    InwardA = inwardA,

                    InwardB = inwardB,

                    AngleDegrees = angleDegrees

                };



            joints.Add(joint);

        }





        // ============================================================

        // ПОЛУЧИТЬ START / END СТЕНЫ

        // ============================================================



        private static XYZ GetWallEndPoint(

            WallData wall,

            WallEndSide side)

        {

            if (side == WallEndSide.Start)

            {

                return wall.StartPoint;

            }



            return wall.EndPoint;

        }





        // ============================================================

        // НАПРАВЛЕНИЕ ОТ УЗЛА ВНУТРЬ СТЕНЫ

        //

        // START -> +Direction

        // END   -> -Direction

        // ============================================================



        private static XYZ GetWallInwardDirection(

            WallData wall,

            WallEndSide side)

        {

            XYZ direction =

                wall.Direction.Normalize();



            if (side == WallEndSide.Start)

            {

                return direction;

            }



            return -direction;

        }





        // ============================================================

        // УГОЛ МЕЖДУ СТЕНАМИ

        // ============================================================



        private static double GetWallAngleDegrees(

            WallData wallA,

            WallData wallB)

        {

            XYZ directionA =

                wallA.Direction.Normalize();



            XYZ directionB =

                wallB.Direction.Normalize();



            double dot =

                Math.Abs(

                    directionA.DotProduct(

                        directionB));



            if (dot > 1.0)

            {

                dot = 1.0;

            }



            if (dot < -1.0)

            {

                dot = -1.0;

            }



            double angleRadians =

                Math.Acos(dot);



            double angleDegrees =

                angleRadians

                * 180.0

                / Math.PI;



            return angleDegrees;

        }





        // ============================================================

        // РАССТОЯНИЕ В ПЛАНЕ

        // ============================================================



        private static double GetPlanDistance(

            XYZ pointA,

            XYZ pointB)

        {

            double dx =

                pointA.X -

                pointB.X;



            double dy =

                pointA.Y -

                pointB.Y;



            return Math.Sqrt(

                dx * dx +

                dy * dy);

        }





        // ============================================================

        // СТЕНЫ НА ОДНОМ ЭТАЖЕ?

        // ============================================================



        private static bool AreWallsOnSameLevel(

            WallData wallA,

            WallData wallB)

        {

            double tolerance =

                ToFeet(5.0);



            bool sameBottom =

                Math.Abs(

                    wallA.BottomZ -

                    wallB.BottomZ)

                <= tolerance;



            bool sameTop =

                Math.Abs(

                    wallA.TopZ -

                    wallB.TopZ)

                <= tolerance;



            return

                sameBottom &&

                sameTop;

        }





        // ============================================================

        // УДАЛЕНИЕ ДУБЛИКАТОВ УЗЛОВ

        // ============================================================



        private static List<WallCornerJoint>

            RemoveDuplicateCornerJoints(

                List<WallCornerJoint> joints)

        {

            List<WallCornerJoint> result =

                new List<WallCornerJoint>();



            double pointTolerance =

                ToFeet(10.0);



            foreach (

                WallCornerJoint joint

                in joints)

            {

                bool duplicate =

                    result.Any(existing =>

                    {

                        bool sameWalls =

                            (

                                existing.WallA.Wall.Id ==

                                joint.WallA.Wall.Id

                                &&

                                existing.WallB.Wall.Id ==

                                joint.WallB.Wall.Id

                            )

                            ||

                            (

                                existing.WallA.Wall.Id ==

                                joint.WallB.Wall.Id

                                &&

                                existing.WallB.Wall.Id ==

                                joint.WallA.Wall.Id

                            );



                        if (!sameWalls)

                        {

                            return false;

                        }



                        double distance =

                            GetPlanDistance(

                                existing.JointPoint,

                                joint.JointPoint);



                        return

                            distance <=

                            pointTolerance;

                    });



                if (!duplicate)

                {

                    result.Add(

                        joint);

                }

            }



            return result;

        }





        // ============================================================

        // ЯВЛЯЕТСЯ ЛИ START / END ЧАСТЬЮ Г-ОБРАЗНОГО УЗЛА

        // ============================================================



        private static bool IsWallEndInCorner(

            WallData wall,

            WallEndSide side,

            List<WallData> allWalls)

        {

            List<WallCornerJoint> joints =

                FindCornerJoints(

                    allWalls);



            foreach (

                WallCornerJoint joint

                in joints)

            {

                if (

                    joint.WallA.Wall.Id ==

                    wall.Wall.Id

                    &&

                    joint.SideA == side)

                {

                    return true;

                }



                if (

                    joint.WallB.Wall.Id ==

                    wall.Wall.Id

                    &&

                    joint.SideB == side)

                {

                    return true;

                }

            }



            return false;

        }





        // ============================================================

        // ПРОДЛЕНИЕ ГОРИЗОНТАЛЬНОЙ АРМАТУРЫ

        // В Г-ОБРАЗНОМ УЗЛЕ

        //

        // Берём реальные четыре угла соседней стены.

        // Проецируем их на направление текущей стены.

        //

        // Стержень заканчивается за 20 мм

        // до дальней бетонной грани.

        // ============================================================



        private static void ApplyCornerHorizontalExtensions(

            List<WallData> walls)

        {

            if (walls == null)

            {

                return;

            }



            foreach (WallData wall in walls)

            {

                wall.StartHorizontalExtension = 0.0;

                wall.EndHorizontalExtension = 0.0;

            }



            List<WallCornerJoint> joints =

                FindCornerJoints(walls);



            double concreteOffset =

                ToFeet(

                    HorizontalConcreteEdgeOffsetMm);



            foreach (WallCornerJoint joint in joints)

            {

                double extensionA =

                    CalculateHorizontalExtension(

                        joint.WallA,

                        joint.SideA,

                        joint.WallB,

                        concreteOffset);



                double extensionB =

                    CalculateHorizontalExtension(

                        joint.WallB,

                        joint.SideB,

                        joint.WallA,

                        concreteOffset);



                SetHorizontalExtension(

                    joint.WallA,

                    joint.SideA,

                    extensionA);



                SetHorizontalExtension(

                    joint.WallB,

                    joint.SideB,

                    extensionB);

            }

        }





        // ============================================================

        // РАСЧЁТ ВЫХОДА ЗА ТОРЕЦ

        // ============================================================



        private static double CalculateHorizontalExtension(

            WallData currentWall,

            WallEndSide currentSide,

            WallData neighbourWall,

            double concreteOffset)

        {

            XYZ currentEnd =

                GetWallEndPoint(

                    currentWall,

                    currentSide);



            XYZ outward;



            if (currentSide == WallEndSide.Start)

            {

                outward =

                    -currentWall.Direction.Normalize();

            }

            else

            {

                outward =

                    currentWall.Direction.Normalize();

            }



            List<XYZ> corners =

                GetWallPlanCorners(

                    neighbourWall);



            double maxProjection =

                double.MinValue;



            foreach (XYZ corner in corners)

            {

                XYZ delta =

                    new XYZ(

                        corner.X - currentEnd.X,

                        corner.Y - currentEnd.Y,

                        0);



                double projection =

                    delta.DotProduct(

                        outward);



                if (projection > maxProjection)

                {

                    maxProjection =

                        projection;

                }

            }



            if (maxProjection <= 0.0)

            {

                return 0.0;

            }



            double extension =

                maxProjection -

                concreteOffset;



            if (extension < 0.0)

            {

                extension = 0.0;

            }



            return extension;

        }





        // ============================================================

        // 4 УГЛА БЕТОННОГО ПРЯМОУГОЛЬНИКА СТЕНЫ В ПЛАНЕ

        // ============================================================



        private static List<XYZ> GetWallPlanCorners(

            WallData wall)

        {

            XYZ normal =

                wall.Normal.Normalize();



            double halfThickness =

                wall.Thickness / 2.0;



            XYZ startPlus =

                wall.StartPoint

                + normal * halfThickness;



            XYZ startMinus =

                wall.StartPoint

                - normal * halfThickness;



            XYZ endPlus =

                wall.EndPoint

                + normal * halfThickness;



            XYZ endMinus =

                wall.EndPoint

                - normal * halfThickness;



            return new List<XYZ>

            {

                startPlus,

                startMinus,

                endPlus,

                endMinus

            };

        }





        // ============================================================

        // ЗАПИСЬ В START / END EXTENSION

        // ============================================================



        private static void SetHorizontalExtension(

            WallData wall,

            WallEndSide side,

            double extension)

        {

            if (side == WallEndSide.Start)

            {

                wall.StartHorizontalExtension =

                    Math.Max(

                        wall.StartHorizontalExtension,

                        extension);

            }

            else

            {

                wall.EndHorizontalExtension =

                    Math.Max(

                        wall.EndHorizontalExtension,

                        extension);

            }

        }





        // ============================================================

        // ДИАГНОСТИЧЕСКИЙ ОТЧЁТ

        //

        // Сейчас выводим максимум геометрии.

        // По этому отчёту точно определим положение 4Ø12.

        // ============================================================



        private static string BuildCornerJointReport(

            List<WallData> walls)

        {

            List<WallCornerJoint> joints =

                FindCornerJoints(walls);



            StringBuilder report =

                new StringBuilder();



            report.AppendLine(

                "Г-ОБРАЗНЫЕ УЗЛЫ");



            report.AppendLine(

                "============================");



            report.AppendLine(

                $"Найдено: {joints.Count}");



            report.AppendLine();



            if (joints.Count == 0)

            {

                report.AppendLine(

                    "Г-образные соединения стен не найдены.");



                return report.ToString();

            }



            for (

                int i = 0;

                i < joints.Count;

                i++)

            {

                WallCornerJoint joint =

                    joints[i];



                WallData a =

                    joint.WallA;



                WallData b =

                    joint.WallB;



                report.AppendLine(

                    $"УЗЕЛ #{i + 1}");



                report.AppendLine(

                    "----------------------------");





                // ====================================================

                // СТЕНА A

                // ====================================================



                report.AppendLine(

                    $"СТЕНА A ID: {a.Wall.Id.IntegerValue}");



                report.AppendLine(

                    $"Конец A: {joint.SideA}");



                report.AppendLine(

                    $"Толщина A: {ToMm(a.Thickness):F0} мм");



                report.AppendLine(

                    $"Длина A: {ToMm(a.Length):F0} мм");



                report.AppendLine(

                    $"A Start X = {ToMm(a.StartPoint.X):F0} мм");



                report.AppendLine(

                    $"A Start Y = {ToMm(a.StartPoint.Y):F0} мм");



                report.AppendLine(

                    $"A End X = {ToMm(a.EndPoint.X):F0} мм");



                report.AppendLine(

                    $"A End Y = {ToMm(a.EndPoint.Y):F0} мм");



                report.AppendLine(

                    $"A Direction = {FormatVector(a.Direction)}");



                report.AppendLine(

                    $"A Normal = {FormatVector(a.Normal)}");



                report.AppendLine(

                    $"A Inward = {FormatVector(joint.InwardA)}");



                report.AppendLine();





                // ====================================================

                // СТЕНА B

                // ====================================================



                report.AppendLine(

                    $"СТЕНА B ID: {b.Wall.Id.IntegerValue}");



                report.AppendLine(

                    $"Конец B: {joint.SideB}");



                report.AppendLine(

                    $"Толщина B: {ToMm(b.Thickness):F0} мм");



                report.AppendLine(

                    $"Длина B: {ToMm(b.Length):F0} мм");



                report.AppendLine(

                    $"B Start X = {ToMm(b.StartPoint.X):F0} мм");



                report.AppendLine(

                    $"B Start Y = {ToMm(b.StartPoint.Y):F0} мм");



                report.AppendLine(

                    $"B End X = {ToMm(b.EndPoint.X):F0} мм");



                report.AppendLine(

                    $"B End Y = {ToMm(b.EndPoint.Y):F0} мм");



                report.AppendLine(

                    $"B Direction = {FormatVector(b.Direction)}");



                report.AppendLine(

                    $"B Normal = {FormatVector(b.Normal)}");



                report.AppendLine(

                    $"B Inward = {FormatVector(joint.InwardB)}");



                report.AppendLine();





                // ====================================================

                // JOINT POINT

                // ====================================================



                report.AppendLine(

                    "JOINT POINT:");



                report.AppendLine(

                    $"X = {ToMm(joint.JointPoint.X):F0} мм");



                report.AppendLine(

                    $"Y = {ToMm(joint.JointPoint.Y):F0} мм");



                report.AppendLine();





                // ====================================================

                // ФИЗИЧЕСКИЙ ТОРЕЦ A

                // ====================================================



                XYZ aEnd =

                    GetWallEndPoint(

                        a,

                        joint.SideA);



                XYZ aPlus =

                    aEnd

                    + a.Normal.Normalize()

                    * (a.Thickness / 2.0);



                XYZ aMinus =

                    aEnd

                    - a.Normal.Normalize()

                    * (a.Thickness / 2.0);



                report.AppendLine(

                    "ТОРЕЦ A:");



                report.AppendLine(

                    $"A+ X={ToMm(aPlus.X):F0}; " +

                    $"Y={ToMm(aPlus.Y):F0}");



                report.AppendLine(

                    $"A- X={ToMm(aMinus.X):F0}; " +

                    $"Y={ToMm(aMinus.Y):F0}");



                report.AppendLine();





                // ====================================================

                // ФИЗИЧЕСКИЙ ТОРЕЦ B

                // ====================================================



                XYZ bEnd =

                    GetWallEndPoint(

                        b,

                        joint.SideB);



                XYZ bPlus =

                    bEnd

                    + b.Normal.Normalize()

                    * (b.Thickness / 2.0);



                XYZ bMinus =

                    bEnd

                    - b.Normal.Normalize()

                    * (b.Thickness / 2.0);



                report.AppendLine(

                    "ТОРЕЦ B:");



                report.AppendLine(

                    $"B+ X={ToMm(bPlus.X):F0}; " +

                    $"Y={ToMm(bPlus.Y):F0}");



                report.AppendLine(

                    $"B- X={ToMm(bMinus.X):F0}; " +

                    $"Y={ToMm(bMinus.Y):F0}");



                report.AppendLine();





                // ====================================================

                // 4 УГЛА A

                // ====================================================



                List<XYZ> cornersA =

                    GetWallPlanCorners(a);



                report.AppendLine(

                    "4 УГЛА СТЕНЫ A:");



                for (

                    int c = 0;

                    c < cornersA.Count;

                    c++)

                {

                    report.AppendLine(

                        $"A{c + 1}: " +

                        $"X={ToMm(cornersA[c].X):F0}; " +

                        $"Y={ToMm(cornersA[c].Y):F0}");

                }



                report.AppendLine();





                // ====================================================

                // 4 УГЛА B

                // ====================================================



                List<XYZ> cornersB =

                    GetWallPlanCorners(b);



                report.AppendLine(

                    "4 УГЛА СТЕНЫ B:");



                for (

                    int c = 0;

                    c < cornersB.Count;

                    c++)

                {

                    report.AppendLine(

                        $"B{c + 1}: " +

                        $"X={ToMm(cornersB[c].X):F0}; " +

                        $"Y={ToMm(cornersB[c].Y):F0}");

                }



                report.AppendLine();





                // ====================================================

                // EXTENSION

                // ====================================================



                double extensionA =

                    CalculateHorizontalExtension(

                        a,

                        joint.SideA,

                        b,

                        ToFeet(

                            HorizontalConcreteEdgeOffsetMm));



                double extensionB =

                    CalculateHorizontalExtension(

                        b,

                        joint.SideB,

                        a,

                        ToFeet(

                            HorizontalConcreteEdgeOffsetMm));



                report.AppendLine(

                    $"Extension A = {ToMm(extensionA):F0} мм");



                report.AppendLine(

                    $"Extension B = {ToMm(extensionB):F0} мм");



                report.AppendLine(

                    $"Угол = {joint.AngleDegrees:F1}°");



                report.AppendLine(

                    "============================");



                report.AppendLine();

            }



            return report.ToString();

        }





        // ============================================================

        // ФОРМАТ ВЕКТОРА

        // ============================================================




        // ============================================================
        // ДАННЫЕ Т-ОБРАЗНОГО УЗЛА
        // ============================================================

        private class WallTJoint
        {
            public WallData MainWall;
            public WallData BranchWall;
            public WallEndSide BranchSide;
            public XYZ JointPoint;
            public double PositionOnMain;
            public XYZ BranchInward;
            public double AngleDegrees;
        }


        // ============================================================
        // ПОИСК ВСЕХ Т-ОБРАЗНЫХ УЗЛОВ
        //
        // Только диагностика геометрии.
        // Арматуру здесь не создаём.
        // ============================================================

        private static List<WallTJoint> FindTJoints(
            List<WallData> walls)
        {
            List<WallTJoint> joints =
                new List<WallTJoint>();

            if (walls == null || walls.Count < 2)
            {
                return joints;
            }

            for (int i = 0; i < walls.Count; i++)
            {
                WallData mainWall = walls[i];

                for (int j = 0; j < walls.Count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    WallData branchWall = walls[j];

                    if (!AreWallsOnSameLevel(
                        mainWall,
                        branchWall))
                    {
                        continue;
                    }

                    double angleDegrees =
                        GetWallAngleDegrees(
                            mainWall,
                            branchWall);

                    if (
                        angleDegrees < 80.0 ||
                        angleDegrees > 100.0)
                    {
                        continue;
                    }

                    TryAddTJoint(
                        joints,
                        mainWall,
                        branchWall,
                        WallEndSide.Start,
                        angleDegrees);

                    TryAddTJoint(
                        joints,
                        mainWall,
                        branchWall,
                        WallEndSide.End,
                        angleDegrees);
                }
            }

            List<WallTJoint> uniqueJoints =
                RemoveDuplicateTJoints(joints);

            return RemoveCrossIntersectionsFromTJoints(
                uniqueJoints);
        }


        // ============================================================
        // ИСКЛЮЧАЕМ КРЕСТООБРАЗНЫЕ (+) ПЕРЕСЕЧЕНИЯ ИЗ Т-УЗЛОВ
        //
        // Если к одной и той же MAIN-стене в одной точке подходят
        // два противоположных торца стен, это уже не Т-узел, а +.
        //
        // Такие записи не должны попадать в Т-логику:
        // - не создаём Т-П-шки;
        // - не применяем T-extension;
        // - не создаём несколько комплектов T 4Ø12.
        // ============================================================

        private static List<WallTJoint>
            RemoveCrossIntersectionsFromTJoints(
                List<WallTJoint> joints)
        {
            if (joints == null || joints.Count < 2)
            {
                return joints ?? new List<WallTJoint>();
            }

            double pointTolerance =
                ToFeet(20.0);

            List<WallTJoint> result =
                new List<WallTJoint>();

            foreach (WallTJoint joint in joints)
            {
                bool isCross =
                    joints.Any(other =>
                    {
                        if (ReferenceEquals(other, joint))
                        {
                            return false;
                        }

                        if (other.MainWall.Wall.Id !=
                            joint.MainWall.Wall.Id)
                        {
                            return false;
                        }

                        if (other.BranchWall.Wall.Id ==
                            joint.BranchWall.Wall.Id)
                        {
                            return false;
                        }

                        if (GetPlanDistance(
                            other.JointPoint,
                            joint.JointPoint) >
                            pointTolerance)
                        {
                            return false;
                        }

                        XYZ d1 =
                            joint.BranchInward.Normalize();

                        XYZ d2 =
                            other.BranchInward.Normalize();

                        double dot =
                            d1.DotProduct(d2);

                        // Противоположные направления:
                        // угол примерно 180°.
                        return dot < -0.95;
                    });

                if (!isCross)
                {
                    result.Add(joint);
                }
            }

            return result;
        }


        private static void TryAddTJoint(
            List<WallTJoint> joints,
            WallData mainWall,
            WallData branchWall,
            WallEndSide branchSide,
            double angleDegrees)
        {
            XYZ branchEnd =
                GetWallEndPoint(
                    branchWall,
                    branchSide);

            XYZ mainStart =
                mainWall.StartPoint;

            XYZ mainDirection =
                mainWall.Direction.Normalize();

            XYZ delta =
                new XYZ(
                    branchEnd.X - mainStart.X,
                    branchEnd.Y - mainStart.Y,
                    0.0);

            double positionOnMain =
                delta.DotProduct(mainDirection);

            XYZ projectedPoint =
                new XYZ(
                    mainStart.X,
                    mainStart.Y,
                    mainWall.BottomZ)
                + mainDirection * positionOnMain;

            // Не допускаем попадание в START/END основной стены:
            // эти случаи остаются за логикой Г-узла.
            double endExclusion =
                mainWall.Thickness / 2.0
                + branchWall.Thickness / 2.0
                + ToFeet(10.0);

            if (
                positionOnMain <= endExclusion ||
                positionOnMain >=
                    mainWall.Length - endExclusion)
            {
                return;
            }

            double distanceToMainAxis =
                GetPlanDistance(
                    branchEnd,
                    projectedPoint);

            double allowedDistance =
                mainWall.Thickness / 2.0
                + branchWall.Thickness / 2.0
                + ToFeet(10.0);

            if (distanceToMainAxis > allowedDistance)
            {
                return;
            }

            WallTJoint joint =
                new WallTJoint
                {
                    MainWall = mainWall,
                    BranchWall = branchWall,
                    BranchSide = branchSide,
                    JointPoint =
                        new XYZ(
                            projectedPoint.X,
                            projectedPoint.Y,
                            mainWall.BottomZ),
                    PositionOnMain = positionOnMain,
                    BranchInward =
                        GetWallInwardDirection(
                            branchWall,
                            branchSide),
                    AngleDegrees = angleDegrees
                };

            joints.Add(joint);
        }


        private static List<WallTJoint> RemoveDuplicateTJoints(
            List<WallTJoint> joints)
        {
            List<WallTJoint> result =
                new List<WallTJoint>();

            double pointTolerance =
                ToFeet(10.0);

            foreach (WallTJoint joint in joints)
            {
                bool duplicate =
                    result.Any(existing =>
                        existing.MainWall.Wall.Id ==
                            joint.MainWall.Wall.Id
                        &&
                        existing.BranchWall.Wall.Id ==
                            joint.BranchWall.Wall.Id
                        &&
                        GetPlanDistance(
                            existing.JointPoint,
                            joint.JointPoint)
                            <= pointTolerance);

                if (!duplicate)
                {
                    result.Add(joint);
                }
            }

            return result;
        }


        // ============================================================
        // Т-УЗЕЛ ДЛЯ КОНКРЕТНОГО START / END ПРИМЫКАЮЩЕЙ СТЕНЫ
        // ============================================================

        private static WallTJoint GetTJointForBranchEnd(
            WallData wall,
            WallEndSide side,
            List<WallData> allWalls)
        {
            List<WallTJoint> joints = FindTJoints(allWalls);

            return joints.FirstOrDefault(x =>
                x.BranchWall.Wall.Id == wall.Wall.Id
                && x.BranchSide == side);
        }

        private static bool IsWallEndInTJoint(
            WallData wall,
            WallEndSide side,
            List<WallData> allWalls)
        {
            return GetTJointForBranchEnd(wall, side, allWalls) != null;
        }

        // ============================================================
        // СВОБОДНЫЙ ЛИ ТОРЕЦ СТЕНЫ
        // ============================================================

        private static bool IsWallEndFree(
            WallData wall,
            WallEndSide side,
            List<WallData> allWalls)
        {
            if (wall == null || allWalls == null)
            {
                return false;
            }

            // Г-образный узел.
            if (IsWallEndInCorner(wall, side, allWalls))
            {
                return false;
            }

            // Используем СЫРЫЕ Т-кандидаты, потому что крест (+)
            // специально исключён из обычного FindTJoints().
            // Поэтому торец в + также не будет ошибочно признан свободным.
            List<WallTJoint> rawJoints =
                FindRawTJointsForCrossDetection(allWalls);

            bool inPerpendicularJoint =
                rawJoints.Any(x =>
                    x.BranchWall.Wall.Id == wall.Wall.Id
                    && x.BranchSide == side);

            return !inPerpendicularJoint;
        }


        // ============================================================
        // ПРОДЛЕНИЕ ГОРИЗОНТАЛЬНОЙ Ø10 В Т-УЗЛЕ
        // ============================================================

        private static double CalculateTBranchHorizontalExtension(
            WallTJoint joint)
        {
            WallData branch = joint.BranchWall;
            WallData main = joint.MainWall;

            XYZ branchEnd =
                GetWallEndPoint(branch, joint.BranchSide);

            XYZ outward =
                joint.BranchSide == WallEndSide.Start
                ? -branch.Direction.Normalize()
                : branch.Direction.Normalize();

            List<XYZ> mainCorners =
                GetWallPlanCorners(main);

            double maxProjection = double.MinValue;

            foreach (XYZ corner in mainCorners)
            {
                XYZ delta =
                    new XYZ(
                        corner.X - branchEnd.X,
                        corner.Y - branchEnd.Y,
                        0.0);

                double projection =
                    delta.DotProduct(outward);

                if (projection > maxProjection)
                {
                    maxProjection = projection;
                }
            }

            if (maxProjection <= 0.0)
            {
                return 0.0;
            }

            double extension =
                maxProjection
                - ToFeet(HorizontalConcreteEdgeOffsetMm);

            return Math.Max(0.0, extension);
        }

        // ============================================================
        // ЗАПИСЬ T-EXTENSION В WallData
        // ============================================================

        private static void ApplyTHorizontalExtensions(
            List<WallData> walls)
        {
            if (walls == null)
            {
                return;
            }

            // Обычные Т-узлы.
            List<WallTJoint> joints = FindTJoints(walls);

            foreach (WallTJoint joint in joints)
            {
                double extension =
                    CalculateTBranchHorizontalExtension(joint);

                SetHorizontalExtension(
                    joint.BranchWall,
                    joint.BranchSide,
                    extension);
            }

            // Крестообразные (+) узлы.
            //
            // Они специально исключены из FindTJoints(), чтобы:
            // - не создавать Т-П-шки;
            // - не дублировать Т-узловую арматуру.
            //
            // Но горизонтальная Ø10 примыкающих стен должна пройти
            // через толщину пересекаемой стены до противоположной
            // бетонной грани с обычным отступом 20 мм.
            ApplyCrossHorizontalExtensions(walls);
        }


        // ============================================================
        // ГОРИЗОНТАЛЬНАЯ Ø10 В КРЕСТООБРАЗНОМ (+) УЗЛЕ
        // ============================================================

        private static void ApplyCrossHorizontalExtensions(
            List<WallData> walls)
        {
            List<WallTJoint> rawJoints =
                FindRawTJointsForCrossDetection(walls);

            if (rawJoints == null || rawJoints.Count < 2)
            {
                return;
            }

            double pointTolerance =
                ToFeet(20.0);

            for (int i = 0; i < rawJoints.Count; i++)
            {
                WallTJoint a = rawJoints[i];

                for (int j = i + 1; j < rawJoints.Count; j++)
                {
                    WallTJoint b = rawJoints[j];

                    if (a.MainWall.Wall.Id != b.MainWall.Wall.Id)
                    {
                        continue;
                    }

                    if (a.BranchWall.Wall.Id == b.BranchWall.Wall.Id)
                    {
                        continue;
                    }

                    if (GetPlanDistance(
                        a.JointPoint,
                        b.JointPoint) > pointTolerance)
                    {
                        continue;
                    }

                    XYZ inwardA =
                        a.BranchInward.Normalize();

                    XYZ inwardB =
                        b.BranchInward.Normalize();

                    // Два противоположных BRANCH-торца = крест (+).
                    if (inwardA.DotProduct(inwardB) >= -0.95)
                    {
                        continue;
                    }

                    double extensionA =
                        CalculateTBranchHorizontalExtension(a);

                    double extensionB =
                        CalculateTBranchHorizontalExtension(b);

                    SetHorizontalExtension(
                        a.BranchWall,
                        a.BranchSide,
                        extensionA);

                    SetHorizontalExtension(
                        b.BranchWall,
                        b.BranchSide,
                        extensionB);
                }
            }
        }


        // ============================================================
        // СЫРЫЕ Т-КАНДИДАТЫ ТОЛЬКО ДЛЯ ПОИСКА КРЕСТА
        //
        // ВАЖНО:
        // этот метод НЕ используется PBarService и НЕ возвращает
        // крест обратно в обычную Т-логику.
        // ============================================================

        private static List<WallTJoint> FindRawTJointsForCrossDetection(
            List<WallData> walls)
        {
            List<WallTJoint> joints =
                new List<WallTJoint>();

            if (walls == null || walls.Count < 2)
            {
                return joints;
            }

            for (int i = 0; i < walls.Count; i++)
            {
                WallData mainWall = walls[i];

                for (int j = 0; j < walls.Count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    WallData branchWall = walls[j];

                    if (!AreWallsOnSameLevel(
                        mainWall,
                        branchWall))
                    {
                        continue;
                    }

                    double angleDegrees =
                        GetWallAngleDegrees(
                            mainWall,
                            branchWall);

                    if (angleDegrees < 80.0 ||
                        angleDegrees > 100.0)
                    {
                        continue;
                    }

                    TryAddTJoint(
                        joints,
                        mainWall,
                        branchWall,
                        WallEndSide.Start,
                        angleDegrees);

                    TryAddTJoint(
                        joints,
                        mainWall,
                        branchWall,
                        WallEndSide.End,
                        angleDegrees);
                }
            }

            return RemoveDuplicateTJoints(joints);
        }


        // ============================================================
        // КРЕСТООБРАЗНЫЙ (+) УЗЕЛ
        // ============================================================

        private class WallCrossJoint
        {
            public WallData MainWall;
            public WallData BranchA;
            public WallEndSide SideA;
            public WallData BranchB;
            public WallEndSide SideB;
            public XYZ JointPoint;
        }

        private static List<WallCrossJoint> FindCrossJoints(
            List<WallData> walls)
        {
            List<WallCrossJoint> result = new List<WallCrossJoint>();
            List<WallTJoint> raw = FindRawTJointsForCrossDetection(walls);

            if (raw == null || raw.Count < 2)
                return result;

            double pointTolerance = ToFeet(20.0);

            for (int i = 0; i < raw.Count; i++)
            {
                for (int j = i + 1; j < raw.Count; j++)
                {
                    WallTJoint a = raw[i];
                    WallTJoint b = raw[j];

                    if (a.MainWall.Wall.Id != b.MainWall.Wall.Id)
                        continue;
                    if (a.BranchWall.Wall.Id == b.BranchWall.Wall.Id)
                        continue;
                    if (GetPlanDistance(a.JointPoint, b.JointPoint) > pointTolerance)
                        continue;
                    if (a.BranchInward.Normalize().DotProduct(
                        b.BranchInward.Normalize()) >= -0.95)
                        continue;

                    XYZ jointPoint = new XYZ(
                        (a.JointPoint.X + b.JointPoint.X) / 2.0,
                        (a.JointPoint.Y + b.JointPoint.Y) / 2.0,
                        a.MainWall.BottomZ);

                    bool duplicate = result.Any(x =>
                        x.MainWall.Wall.Id == a.MainWall.Wall.Id &&
                        GetPlanDistance(x.JointPoint, jointPoint) <= pointTolerance);

                    if (duplicate)
                        continue;

                    result.Add(new WallCrossJoint
                    {
                        MainWall = a.MainWall,
                        BranchA = a.BranchWall,
                        SideA = a.BranchSide,
                        BranchB = b.BranchWall,
                        SideB = b.BranchSide,
                        JointPoint = jointPoint
                    });
                }
            }

            return result;
        }

        private static bool IsHorizontalRangeAtCrossEnd(
            WallData wall,
            HorizontalRange range,
            List<WallData> allWalls)
        {
            double tol = ToFeet(1.0);

            foreach (WallCrossJoint cross in FindCrossJoints(allWalls))
            {
                if (cross.BranchA.Wall.Id == wall.Wall.Id)
                {
                    if (cross.SideA == WallEndSide.Start && range.Start <= tol)
                        return true;
                    if (cross.SideA == WallEndSide.End && range.End >= wall.Length - tol)
                        return true;
                }

                if (cross.BranchB.Wall.Id == wall.Wall.Id)
                {
                    if (cross.SideB == WallEndSide.Start && range.Start <= tol)
                        return true;
                    if (cross.SideB == WallEndSide.End && range.End >= wall.Length - tol)
                        return true;
                }
            }

            return false;
        }

        private static bool IsPointInsideCrossJointZone(
            WallData wall,
            XYZ point,
            List<WallData> allWalls)
        {
            double tol = ToFeet(1.0);

            foreach (WallCrossJoint cross in FindCrossJoints(allWalls))
            {
                bool participates =
                    cross.MainWall.Wall.Id == wall.Wall.Id ||
                    cross.BranchA.Wall.Id == wall.Wall.Id ||
                    cross.BranchB.Wall.Id == wall.Wall.Id;

                if (!participates)
                    continue;

                XYZ mainDir = new XYZ(
                    cross.MainWall.Direction.X,
                    cross.MainWall.Direction.Y,
                    0.0).Normalize();

                XYZ branchDir = new XYZ(
                    cross.BranchA.Direction.X,
                    cross.BranchA.Direction.Y,
                    0.0).Normalize();

                XYZ delta = new XYZ(
                    point.X - cross.JointPoint.X,
                    point.Y - cross.JointPoint.Y,
                    0.0);

                double alongMain = Math.Abs(delta.DotProduct(mainDir));
                double alongBranch = Math.Abs(delta.DotProduct(branchDir));

                if (alongMain <= cross.BranchA.Thickness / 2.0 + tol &&
                    alongBranch <= cross.MainWall.Thickness / 2.0 + tol)
                    return true;
            }

            return false;
        }

        // ============================================================
        // ДИАГНОСТИЧЕСКИЙ ОТЧЁТ Т-УЗЛОВ
        // ============================================================

        private static string BuildTJointReport(
            List<WallData> walls)
        {
            List<WallTJoint> joints =
                FindTJoints(walls);

            StringBuilder report =
                new StringBuilder();

            report.AppendLine("Т-ОБРАЗНЫЕ УЗЛЫ");
            report.AppendLine("============================");
            report.AppendLine($"Найдено: {joints.Count}");
            report.AppendLine();

            if (joints.Count == 0)
            {
                report.AppendLine(
                    "Т-образные соединения стен не найдены.");

                return report.ToString();
            }

            for (int i = 0; i < joints.Count; i++)
            {
                WallTJoint joint = joints[i];

                report.AppendLine($"УЗЕЛ #{i + 1}");
                report.AppendLine("----------------------------");
                report.AppendLine(
                    $"MAIN ID: {joint.MainWall.Wall.Id.IntegerValue}");
                report.AppendLine(
                    $"BRANCH ID: {joint.BranchWall.Wall.Id.IntegerValue}");
                report.AppendLine(
                    $"Торец BRANCH: {joint.BranchSide}");
                report.AppendLine(
                    $"Позиция по MAIN: {ToMm(joint.PositionOnMain):F0} мм");
                report.AppendLine(
                    $"X = {ToMm(joint.JointPoint.X):F0} мм");
                report.AppendLine(
                    $"Y = {ToMm(joint.JointPoint.Y):F0} мм");
                report.AppendLine(
                    $"Угол = {joint.AngleDegrees:F1}°");
                report.AppendLine(
                    $"BRANCH inward = {FormatVector(joint.BranchInward)}");
                report.AppendLine("============================");
                report.AppendLine();
            }

            return report.ToString();
        }


        private static string FormatVector(

            XYZ vector)

        {

            return string.Format(

                "({0:F2}; {1:F2}; {2:F2})",

                vector.X,

                vector.Y,

                vector.Z);

        }

    }

}