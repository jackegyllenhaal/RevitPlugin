using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System;
using System.Collections.Generic;

namespace RevitPlugin
{
    public partial class HelloRevit
    {
        // ============================================================
        // 4Ø12 В Т-ОБРАЗНОМ УЗЛЕ
        //
        // MainWall   = основная непрерывная стена.
        // BranchWall = примыкающая стена.
        //
        // Четыре вертикальных стержня ставятся по четырём углам
        // прямоугольной зоны пересечения стен.
        //
        // Ось каждого Ø12:
        // 50 мм от соответствующих бетонных граней.
        // ============================================================

        private static int CreateTJointVerticalBars(
            Document doc,
            List<WallData> allWalls,
            RebarBarType barType,
            double lapMmFor10)
        {
            int created = 0;

            List<WallTJoint> joints =
                FindTJoints(allWalls);

            if (joints == null || joints.Count == 0)
            {
                return 0;
            }

            // Например C25/30:
            // Ø10 = 550 мм
            // Ø12 = 660 мм
            double lap12Mm =
                lapMmFor10 * ToMm(GetBarDiameterFeet(barType)) / 10.0;

            foreach (WallTJoint joint in joints)
            {
                created +=
                    CreateFourTJointVerticalBars(
                        doc,
                        joint,
                        allWalls,
                        barType,
                        lap12Mm);
            }

            return created;
        }


        // ============================================================
        // СОЗДАНИЕ ЧЕТЫРЁХ Ø12 В ОДНОМ Т-УЗЛЕ
        // ============================================================

        private static int CreateFourTJointVerticalBars(
            Document doc,
            WallTJoint joint,
            List<WallData> allWalls,
            RebarBarType barType,
            double lap12Mm)
        {
            WallData main = joint.MainWall;
            WallData branch = joint.BranchWall;

            if (main == null || branch == null)
            {
                return 0;
            }

            double cover = ToFeet(50.0);

            if (main.Thickness <= 2.0 * cover)
            {
                throw new Exception(
                    $"Основная стена ID {main.Wall.Id.IntegerValue}: " +
                    "недостаточная толщина для 4Ø12 Т-узла.");
            }

            if (branch.Thickness <= 2.0 * cover)
            {
                throw new Exception(
                    $"Примыкающая стена ID {branch.Wall.Id.IntegerValue}: " +
                    "недостаточная толщина для 4Ø12 Т-узла.");
            }

            XYZ mainDirection =
                new XYZ(
                    main.Direction.X,
                    main.Direction.Y,
                    0.0)
                .Normalize();

            XYZ branchDirection =
                new XYZ(
                    branch.Direction.X,
                    branch.Direction.Y,
                    0.0)
                .Normalize();

            // Фактическое пересечение осей двух стен.
            // Для Т-узла это надёжнее, чем брать только JointPoint:
            // четыре точки строятся от реальных осей стен.
            XYZ intersection =
                GetInfiniteLineIntersection2D(
                    main.StartPoint,
                    mainDirection,
                    branch.StartPoint,
                    branchDirection);

            if (intersection == null)
            {
                throw new Exception(
                    "Не удалось определить пересечение осей Т-узла: " +
                    $"MAIN ID {main.Wall.Id.IntegerValue}, " +
                    $"BRANCH ID {branch.Wall.Id.IntegerValue}.");
            }

            double halfMain =
                main.Thickness / 2.0;

            double halfBranch =
                branch.Thickness / 2.0;

            // --------------------------------------------------------
            // ЧЕТЫРЕ ТОЧКИ
            //
            // Вдоль MAIN:
            // ±(толщина BRANCH / 2 - 50 мм)
            //
            // Вдоль BRANCH:
            // ±(толщина MAIN / 2 - 50 мм)
            //
            // Таким образом каждая ось Ø12 находится на 50 мм
            // от соответствующих бетонных граней обеих стен.
            // --------------------------------------------------------

            double alongMain =
                halfBranch - cover;

            double alongBranch =
                halfMain - cover;

            XYZ p1 =
                intersection
                + mainDirection * alongMain
                + branchDirection * alongBranch;

            XYZ p2 =
                intersection
                + mainDirection * alongMain
                - branchDirection * alongBranch;

            XYZ p3 =
                intersection
                - mainDirection * alongMain
                + branchDirection * alongBranch;

            XYZ p4 =
                intersection
                - mainDirection * alongMain
                - branchDirection * alongBranch;

            List<XYZ> points =
                new List<XYZ>
                {
                    p1,
                    p2,
                    p3,
                    p4
                };

            points =
                RemoveDuplicatePlanPoints(
                    points,
                    ToFeet(2.0));

            // Для нормального Т-узла должны получить ровно 4 точки.
            if (points.Count != 4)
            {
                throw new Exception(
                    $"Т-узел MAIN {main.Wall.Id.IntegerValue} / " +
                    $"BRANCH {branch.Wall.Id.IntegerValue}: " +
                    $"ожидалось 4 позиции Ø12, получено {points.Count}.");
            }

            // --------------------------------------------------------
            // ВЕРТИКАЛЬНЫЙ ДИАПАЗОН
            //
            // Используем ту же проверенную логику, что у Г-узла.
            // --------------------------------------------------------

            double bottom =
                Math.Min(
                    main.BottomZ,
                    branch.BottomZ);

            double topMain =
                GetCornerVerticalBarTop(
                    doc,
                    main,
                    allWalls,
                    lap12Mm);

            double topBranch =
                GetCornerVerticalBarTop(
                    doc,
                    branch,
                    allWalls,
                    lap12Mm);

            double top =
                Math.Max(
                    topMain,
                    topBranch);

            if (top - bottom < ToFeet(10.0))
            {
                return 0;
            }

            // --------------------------------------------------------
            // СОЗДАЁМ РОВНО 4 Ø12
            //
            // Host = MAIN wall.
            // --------------------------------------------------------

            int created = 0;

            foreach (XYZ point in points)
            {
                if (HasExistingVerticalJointBarAtPoint(
                    doc,
                    barType,
                    point,
                    bottom,
                    top,
                    ToFeet(2.0)))
                {
                    continue;
                }

                created +=
                    CreateCornerVerticalBar(
                        doc,
                        main,
                        barType,
                        point,
                        bottom,
                        top);
            }

            return created;
        }

        private static bool HasExistingVerticalJointBarAtPoint(
            Document doc,
            RebarBarType barType,
            XYZ planPoint,
            double bottom,
            double top,
            double planTolerance)
        {
            FilteredElementCollector collector =
                new FilteredElementCollector(doc)
                    .OfClass(typeof(Rebar));

            foreach (Rebar existing in collector)
            {
                if (existing == null || existing.GetTypeId() != barType.Id)
                    continue;

                IList<Curve> curves;
                try
                {
                    curves = existing.GetCenterlineCurves(
                        false, false, false,
                        MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                }
                catch
                {
                    continue;
                }

                if (curves == null || curves.Count != 1)
                    continue;

                Line line = curves[0] as Line;
                if (line == null)
                    continue;

                XYZ p0 = line.GetEndPoint(0);
                XYZ p1 = line.GetEndPoint(1);
                XYZ d = p1 - p0;

                double planLength =
                    Math.Sqrt(d.X * d.X + d.Y * d.Y);

                if (planLength > ToFeet(2.0))
                    continue;

                double dx = (p0.X + p1.X) / 2.0 - planPoint.X;
                double dy = (p0.Y + p1.Y) / 2.0 - planPoint.Y;

                if (Math.Sqrt(dx * dx + dy * dy) > planTolerance)
                    continue;

                double existingBottom = Math.Min(p0.Z, p1.Z);
                double zTol = ToFeet(2.0);

                // Дублем считаем только стержень, который начинается
                // на ЭТОМ ЖЕ этаже. Нахлёст Ø12 предыдущего этажа
                // может заходить выше BottomZ и не должен блокировать
                // новый комплект 4Ø12 следующего этажа.
                if (Math.Abs(existingBottom - bottom) <= zTol)
                {
                    return true;
                }
            }

            return false;
        }


        // ============================================================
        // 4Ø12 В КРЕСТООБРАЗНОМ (+) УЗЛЕ
        // ============================================================

        private static int CreateCrossVerticalBars(
            Document doc,
            List<WallData> allWalls,
            RebarBarType barType,
            double lapMmFor10)
        {
            int created = 0;
            double lap12Mm = lapMmFor10 * ToMm(GetBarDiameterFeet(barType)) / 10.0;

            foreach (WallCrossJoint cross in FindCrossJoints(allWalls))
            {
                // Для геометрии 4Ø12 достаточно MAIN + одной из
                // двух коллинеарных BRANCH-стен. Получаем ровно
                // четыре угла зоны пересечения, а не два комплекта.
                WallTJoint geometryJoint = new WallTJoint
                {
                    MainWall = cross.MainWall,
                    BranchWall = cross.BranchA,
                    BranchSide = cross.SideA,
                    JointPoint = cross.JointPoint,
                    BranchInward = GetWallInwardDirection(
                        cross.BranchA,
                        cross.SideA),
                    AngleDegrees = 90.0
                };

                created += CreateFourTJointVerticalBars(
                    doc,
                    geometryJoint,
                    allWalls,
                    barType,
                    lap12Mm);
            }

            return created;
        }


    }
}
