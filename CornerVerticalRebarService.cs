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
        // 4Ø12 В Г-ОБРАЗНОМ УЗЛЕ
        //
        // Четыре вертикальных стержня формируются непосредственно
        // из геометрии двух стен.
        //
        // Ось каждого Ø12:
        // 50 мм от соответствующих бетонных граней.
        // ============================================================

        private static int CreateCornerVerticalBars(
            Document doc,
            List<WallData> allWalls,
            RebarBarType barType,
            double lapMmFor10)
        {
            int created = 0;

            List<WallCornerJoint> joints =
                FindCornerJoints(allWalls);

            if (joints == null ||
                joints.Count == 0)
            {
                return 0;
            }

            // Для C25/30:
            // Ø10 = 550 мм
            // Ø12 = 660 мм
            double lap12Mm =
                lapMmFor10 * ToMm(GetBarDiameterFeet(barType)) / 10.0;

            foreach (WallCornerJoint joint in joints)
            {
                created +=
                    CreateFourCornerVerticalBars(
                        doc,
                        joint,
                        allWalls,
                        barType,
                        lap12Mm);
            }

            return created;
        }


        // ============================================================
        // СОЗДАНИЕ ЧЕТЫРЁХ Ø12
        // ============================================================

        private static int CreateFourCornerVerticalBars(
            Document doc,
            WallCornerJoint joint,
            List<WallData> allWalls,
            RebarBarType barType,
            double lap12Mm)
        {
            WallData wallA = joint.WallA;
            WallData wallB = joint.WallB;

            double cover =
                ToFeet(50.0);

            if (wallA.Thickness <= 2.0 * cover)
            {
                throw new Exception(
                    $"Стена ID {wallA.Wall.Id.IntegerValue}: " +
                    "недостаточная толщина для 4Ø12.");
            }

            if (wallB.Thickness <= 2.0 * cover)
            {
                throw new Exception(
                    $"Стена ID {wallB.Wall.Id.IntegerValue}: " +
                    "недостаточная толщина для 4Ø12.");
            }


            // --------------------------------------------------------
            // НАПРАВЛЕНИЯ ОТ УЗЛА ВНУТРЬ СТЕН
            // --------------------------------------------------------

            XYZ inwardA =
                GetWallInwardDirection(
                    wallA,
                    joint.SideA)
                .Normalize();

            XYZ inwardB =
                GetWallInwardDirection(
                    wallB,
                    joint.SideB)
                .Normalize();


            // --------------------------------------------------------
            // ФАКТИЧЕСКАЯ ТОЧКА ПЕРЕСЕЧЕНИЯ ОСЕЙ СТЕН
            // --------------------------------------------------------

            XYZ intersection =
                GetInfiniteLineIntersection2D(
                    wallA.StartPoint,
                    wallA.Direction,
                    wallB.StartPoint,
                    wallB.Direction);

            if (intersection == null)
            {
                throw new Exception(
                    "Не удалось определить пересечение осей стен "
                    + wallA.Wall.Id.IntegerValue
                    + " и "
                    + wallB.Wall.Id.IntegerValue);
            }


            // --------------------------------------------------------
            // РАССТОЯНИЯ ОТ ОСИ ДО БЕТОННЫХ ГРАНЕЙ
            //
            // Для стены A поперечное направление совпадает
            // с направлением стены B.
            //
            // Для стены B поперечное направление совпадает
            // с направлением стены A.
            // --------------------------------------------------------

            double halfA =
                wallA.Thickness / 2.0;

            double halfB =
                wallB.Thickness / 2.0;


            // --------------------------------------------------------
            // ЧЕТЫРЕ ТОЧКИ
            //
            //       P1 -------- P2
            //       |           |
            //       |           |
            //       P3          P4
            //
            // Здесь используются реальные толщины обеих стен.
            // --------------------------------------------------------

            XYZ p1 =
                intersection
                + inwardA * (halfB - cover)
                + inwardB * (halfA - cover);

            XYZ p2 =
                intersection
                + inwardA * (halfB - cover)
                - inwardB * (halfA - cover);

            XYZ p3 =
                intersection
                - inwardA * (halfB - cover)
                + inwardB * (halfA - cover);

            XYZ p4 =
                intersection
                - inwardA * (halfB - cover)
                - inwardB * (halfA - cover);


            List<XYZ> points =
                new List<XYZ>
                {
                    p1,
                    p2,
                    p3,
                    p4
                };


            // --------------------------------------------------------
            // УДАЛЯЕМ СЛУЧАЙНЫЕ ДУБЛИКАТЫ
            // --------------------------------------------------------

            points =
                RemoveDuplicatePlanPoints(
                    points,
                    ToFeet(2.0));


            // --------------------------------------------------------
            // ВЕРТИКАЛЬНЫЙ ДИАПАЗОН
            // --------------------------------------------------------

            double bottom =
                Math.Min(
                    wallA.BottomZ,
                    wallB.BottomZ);

            double topA =
                GetCornerVerticalBarTop(
                    doc,
                    wallA,
                    allWalls,
                    lap12Mm);

            double topB =
                GetCornerVerticalBarTop(
                    doc,
                    wallB,
                    allWalls,
                    lap12Mm);

            double top =
                Math.Max(
                    topA,
                    topB);

            if (top - bottom < ToFeet(10.0))
            {
                return 0;
            }


            // --------------------------------------------------------
            // СОЗДАЁМ РОВНО 4 Ø12
            // --------------------------------------------------------

            int created = 0;

            foreach (XYZ point in points)
            {
                created +=
                    CreateCornerVerticalBar(
                        doc,
                        wallA,
                        barType,
                        point,
                        bottom,
                        top);
            }

            return created;
        }


        // ============================================================
        // ПЕРЕСЕЧЕНИЕ ДВУХ БЕСКОНЕЧНЫХ ЛИНИЙ В XY
        // ============================================================

        private static XYZ GetInfiniteLineIntersection2D(
            XYZ pointA,
            XYZ directionA,
            XYZ pointB,
            XYZ directionB)
        {
            XYZ d1 =
                new XYZ(
                    directionA.X,
                    directionA.Y,
                    0);

            XYZ d2 =
                new XYZ(
                    directionB.X,
                    directionB.Y,
                    0);

            if (d1.GetLength() < 1e-9 ||
                d2.GetLength() < 1e-9)
            {
                return null;
            }

            d1 = d1.Normalize();
            d2 = d2.Normalize();

            double denominator =
                d1.X * d2.Y
                - d1.Y * d2.X;

            if (Math.Abs(denominator) < 1e-9)
            {
                return null;
            }

            double dx =
                pointB.X - pointA.X;

            double dy =
                pointB.Y - pointA.Y;

            double t =
                (dx * d2.Y - dy * d2.X)
                / denominator;

            return new XYZ(
                pointA.X + d1.X * t,
                pointA.Y + d1.Y * t,
                0);
        }


        // ============================================================
        // УДАЛЕНИЕ ДУБЛИКАТОВ ТОЧЕК
        // ============================================================

        private static List<XYZ> RemoveDuplicatePlanPoints(
            List<XYZ> points,
            double tolerance)
        {
            List<XYZ> result =
                new List<XYZ>();

            foreach (XYZ point in points)
            {
                bool duplicate =
                    result.Any(
                        existing =>
                            GetPlanDistance(
                                existing,
                                point)
                            <= tolerance);

                if (!duplicate)
                {
                    result.Add(point);
                }
            }

            return result;
        }


        // ============================================================
        // ВЕРХ Ø12
        // ============================================================

        private static double GetCornerVerticalBarTop(
            Document doc,
            WallData wall,
            List<WallData> allWalls,
            double lap12Mm)
        {
            List<List<WallData>> branches =
                BuildVerticalWallBranches(
                    allWalls);

            foreach (List<WallData> branch in branches)
            {
                for (int i = 0;
                     i < branch.Count;
                     i++)
                {
                    if (branch[i].Wall.Id !=
                        wall.Wall.Id)
                    {
                        continue;
                    }

                    if (i < branch.Count - 1)
                    {
                        return
                            branch[i + 1].BottomZ
                            + ToFeet(lap12Mm);
                    }

                    return
                        GetFinalVerticalBarTop(
                            doc,
                            wall);
                }
            }

            return
                GetFinalVerticalBarTop(
                    doc,
                    wall);
        }


        // ============================================================
        // СОЗДАНИЕ ОДНОГО ВЕРТИКАЛЬНОГО Ø12
        // ============================================================

        private static int CreateCornerVerticalBar(
            Document doc,
            WallData wall,
            RebarBarType barType,
            XYZ planPoint,
            double bottom,
            double top)
        {
            XYZ start =
                new XYZ(
                    planPoint.X,
                    planPoint.Y,
                    bottom);

            XYZ end =
                new XYZ(
                    planPoint.X,
                    planPoint.Y,
                    top);

            List<Curve> curves =
                new List<Curve>
                {
                    Line.CreateBound(
                        start,
                        end)
                };

            Rebar rebar =
                Rebar.CreateFromCurves(
                    doc,
                    RebarStyle.Standard,
                    barType,
                    null,
                    null,
                    wall.Wall,
                    wall.Direction.Normalize(),
                    curves,
                    RebarHookOrientation.Left,
                    RebarHookOrientation.Left,
                    true,
                    true);

            if (rebar == null)
            {
                throw new Exception(
                    $"Не удалось создать узловой Ø12 " +
                    $"в стене ID {wall.Wall.Id.IntegerValue}.");
            }

            return 1;
        }
    }
}
