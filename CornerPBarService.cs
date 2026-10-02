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
        // ВЕРХНИЕ П-ШКИ Г-ОБРАЗНОГО УЗЛА
        //
        // Работают совместно с 4Ø12.
        //
        // В Г-узле создаются ДВЕ П-шки:
        //
        // 1. По ветви wallA
        // 2. По ветви wallB
        //
        // Геометрия 4Ø12 НЕ ИЗМЕНЯЕТСЯ.
        //
        // Ножки = 500 мм вниз.
        // ============================================================

        private static int CreateTopCornerPBars(
            Document doc,
            List<WallData> allWalls)
        {
            int created = 0;


            // ========================================================
            // НАХОДИМ Г-ОБРАЗНЫЕ УЗЛЫ
            // ========================================================

            List<WallCornerJoint> joints =
                FindCornerJoints(allWalls);


            if (joints == null ||
                joints.Count == 0)
            {
                return 0;
            }


            // ========================================================
            // ТИП П-ШКИ
            // ========================================================

            RebarBarType barType =
                FindPBarType(doc);


            double pBarDiameter =
                GetBarDiameterFeet(
                    barType);


            double pBarRadius =
                pBarDiameter / 2.0;


            // ========================================================
            // ЗАЩИТНЫЙ СЛОЙ ДО ОСИ 4Ø12
            // ========================================================

            double cover =
                ToFeet(50.0);


            // ========================================================
            // ОБРАБАТЫВАЕМ КАЖДЫЙ Г-УЗЕЛ
            // ========================================================

            foreach (WallCornerJoint joint in joints)
            {
                WallData wallA =
                    joint.WallA;


                WallData wallB =
                    joint.WallB;


                // ====================================================
                // ВРЕМЕННО НЕ ФИЛЬТРУЕМ ПО ВЕРХНЕМУ ЭТАЖУ.
                //
                // Сначала проверяем правильность обеих П-шек.
                // После подтверждения вернём фильтр верхнего этажа.
                // ====================================================


                // ====================================================
                // НАПРАВЛЕНИЯ ОТ УЗЛА ВНУТРЬ СТЕН
                // ====================================================

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


                // ====================================================
                // ПЕРЕСЕЧЕНИЕ ОСЕЙ ДВУХ СТЕН
                // ====================================================

                XYZ intersection =
                    GetInfiniteLineIntersection2D(
                        wallA.StartPoint,
                        wallA.Direction,
                        wallB.StartPoint,
                        wallB.Direction);


                if (intersection == null)
                {
                    continue;
                }


                // ====================================================
                // ПОЛОВИНЫ ТОЛЩИН СТЕН
                // ====================================================

                double halfA =
                    wallA.Thickness / 2.0;


                double halfB =
                    wallB.Thickness / 2.0;


                // ====================================================
                // ЧЕТЫРЕ ТОЧКИ 4Ø12
                //
                // Используем ТОЧНО ТУ ЖЕ геометрию,
                // что используется для вертикальных 4Ø12.
                //
                // Расположение Ø12 здесь не изменяем.
                // ====================================================

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


                // ====================================================
                // ФАКТИЧЕСКИЙ ВЕРХ БЕТОНА
                // ====================================================

                double concreteTopA =
                    GetTopPBarConcreteTop(
                        doc,
                        wallA);


                double concreteTopB =
                    GetTopPBarConcreteTop(
                        doc,
                        wallB);


                double concreteTop =
                    Math.Max(
                        concreteTopA,
                        concreteTopB);


                // ====================================================
                // УРОВЕНЬ ПЕРЕМЫЧКИ П-ШКИ
                //
                // 20 мм от верха бетона.
                // ====================================================

                double crossZ =
                    concreteTop
                    - ToFeet(20.0)
                    - pBarRadius;


                // ====================================================
                // П-ШКА №1 — WALL A
                //
                // Соединяем:
                //
                // P1 -------- P2
                //
                // Host:
                // wallA
                // ====================================================

                Rebar pBarA =
                    CreateCornerTopPBar(
                        doc,
                        wallA,
                        barType,
                        p1,
                        p2,
                        crossZ);


                if (pBarA != null)
                {
                    created++;
                }


                // ====================================================
                // П-ШКА №2 — WALL B
                //
                // ПЕРПЕНДИКУЛЯРНО ПЕРВОЙ.
                //
                // Соединяем:
                //
                // P1
                // |
                // |
                // P3
                //
                // Host:
                // wallB
                //
                // Именно этой П-шки раньше не было.
                // ====================================================

                Rebar pBarB =
                    CreateCornerTopPBar(
                        doc,
                        wallB,
                        barType,
                        p1,
                        p3,
                        crossZ);


                if (pBarB != null)
                {
                    created++;
                }
            }


            return created;
        }


        // ============================================================
        // СОЗДАНИЕ ОДНОЙ ВЕРХНЕЙ П-ШКИ Г-УЗЛА
        // ============================================================

        private static Rebar CreateCornerTopPBar(
            Document doc,
            WallData hostWall,
            RebarBarType barType,
            XYZ planPointA,
            XYZ planPointB,
            double crossZ)
        {
            // ========================================================
            // ДЛИНА НОЖЕК
            // ========================================================

            double legLength =
                ToFeet(500.0);


            // ========================================================
            // ТОЧКА A ПЕРЕМЫЧКИ
            // ========================================================

            XYZ crossA =
                new XYZ(
                    planPointA.X,
                    planPointA.Y,
                    crossZ);


            // ========================================================
            // ТОЧКА B ПЕРЕМЫЧКИ
            // ========================================================

            XYZ crossB =
                new XYZ(
                    planPointB.X,
                    planPointB.Y,
                    crossZ);


            // ========================================================
            // НОЖКА A — 500 ММ ВНИЗ
            // ========================================================

            XYZ legAEnd =
                crossA
                - XYZ.BasisZ * legLength;


            // ========================================================
            // НОЖКА B — 500 ММ ВНИЗ
            // ========================================================

            XYZ legBEnd =
                crossB
                - XYZ.BasisZ * legLength;


            // ========================================================
            // ГЕОМЕТРИЯ
            //
            //     crossA -------- crossB
            //       |               |
            //       |               |
            //       |               |
            //    legAEnd         legBEnd
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
            // НАПРАВЛЕНИЕ ПЕРЕМЫЧКИ
            // ========================================================

            XYZ crossDirection =
                new XYZ(
                    crossB.X - crossA.X,
                    crossB.Y - crossA.Y,
                    0);


            if (crossDirection.GetLength() < 1e-9)
            {
                return null;
            }


            crossDirection =
                crossDirection.Normalize();


            // ========================================================
            // НОРМАЛЬ ПЛОСКОСТИ П-ШКИ
            // ========================================================

            XYZ planeNormal =
                new XYZ(
                    -crossDirection.Y,
                    crossDirection.X,
                    0);


            // ========================================================
            // СОЗДАНИЕ П-ШКИ
            // ========================================================

            Rebar pBar =
                Rebar.CreateFromCurves(
                    doc,
                    RebarStyle.Standard,
                    barType,
                    null,
                    null,
                    hostWall.Wall,
                    planeNormal,
                    curves,
                    RebarHookOrientation.Left,
                    RebarHookOrientation.Left,
                    true,
                    true);


            if (pBar == null)
            {
                throw new Exception(
                    "Не удалось создать верхнюю П-шку Г-узла.");
            }


            return pBar;
        }


        // ============================================================
        // ПРОВЕРКА ВЕРХНЕГО Г-УЗЛА
        //
        // Метод пока сохраняем.
        //
        // В CreateTopCornerPBars сейчас специально НЕ вызывается,
        // чтобы сначала убедиться, что обе П-шки создаются.
        // ============================================================

        private static bool IsTopCornerJoint(
            WallData wallA,
            WallData wallB,
            List<WallData> allWalls)
        {
            double tolerance =
                ToFeet(5.0);


            foreach (WallData other in allWalls)
            {
                if (other.Wall.Id == wallA.Wall.Id ||
                    other.Wall.Id == wallB.Wall.Id)
                {
                    continue;
                }


                // ====================================================
                // ЕСТЬ ПРОДОЛЖЕНИЕ НАД WALL A?
                // ====================================================

                if (
                    Math.Abs(
                        other.BottomZ -
                        wallA.TopZ)
                    <= tolerance
                    &&
                    AreWallsSameVerticalBranch(
                        wallA,
                        other))
                {
                    return false;
                }


                // ====================================================
                // ЕСТЬ ПРОДОЛЖЕНИЕ НАД WALL B?
                // ====================================================

                if (
                    Math.Abs(
                        other.BottomZ -
                        wallB.TopZ)
                    <= tolerance
                    &&
                    AreWallsSameVerticalBranch(
                        wallB,
                        other))
                {
                    return false;
                }
            }


            return true;
        }


        // ============================================================
        // ПРОВЕРКА ПРИНАДЛЕЖНОСТИ К ОДНОЙ
        // ВЕРТИКАЛЬНОЙ ВЕТВИ
        // ============================================================

        private static bool AreWallsSameVerticalBranch(
            WallData a,
            WallData b)
        {
            // ========================================================
            // СРАВНИВАЕМ НАПРАВЛЕНИЯ
            // ========================================================

            double directionTolerance =
                0.99;


            XYZ dirA =
                a.Direction.Normalize();


            XYZ dirB =
                b.Direction.Normalize();


            double dot =
                Math.Abs(
                    dirA.DotProduct(
                        dirB));


            if (dot < directionTolerance)
            {
                return false;
            }


            // ========================================================
            // СРАВНИВАЕМ ОСИ В ПЛАНЕ
            // ========================================================

            XYZ delta =
                new XYZ(
                    b.StartPoint.X -
                    a.StartPoint.X,

                    b.StartPoint.Y -
                    a.StartPoint.Y,

                    0);


            XYZ normal =
                a.Normal.Normalize();


            double perpendicularDistance =
                Math.Abs(
                    delta.DotProduct(
                        normal));


            double tolerance =
                ToFeet(10.0);


            return
                perpendicularDistance
                <= tolerance;
        }
    }
}