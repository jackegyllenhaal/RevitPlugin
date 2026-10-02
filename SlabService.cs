using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitPlugin
{
    public partial class HelloRevit
    {
        // ============================================================
        // ПОИСК ПЛИТЫ НАД СТЕНОЙ
        // ============================================================

        private static SlabData FindSlabAboveWall(
            Document doc,
            WallData wall)
        {
            double zTolerance =
                ToFeet(100.0);

            double xyTolerance =
                ToFeet(50.0);

            List<Floor> floors =
                new FilteredElementCollector(doc)
                .OfClass(typeof(Floor))
                .Cast<Floor>()
                .ToList();

            SlabData best =
                null;

            double bestDistance =
                double.MaxValue;

            foreach (Floor floor in floors)
            {
                BoundingBoxXYZ box =
                    floor.get_BoundingBox(null);

                if (box == null)
                {
                    continue;
                }

                double slabBottom =
                    box.Min.Z;

                double slabTop =
                    box.Max.Z;

                // ----------------------------------------------------
                // Плита должна начинаться примерно
                // на отметке верха стены.
                // ----------------------------------------------------

                double zDistance =
                    Math.Abs(
                        slabBottom -
                        wall.TopZ);

                // Также допускаем ситуацию,
                // когда верх стены немного входит в плиту.

                bool zOk =
                    zDistance <= zTolerance
                    ||
                    (
                        wall.TopZ >= slabBottom - zTolerance
                        &&
                        wall.TopZ <= slabTop + zTolerance
                    );

                if (!zOk)
                {
                    continue;
                }

                // ----------------------------------------------------
                // Проверяем, что плита действительно
                // находится над данной стеной.
                //
                // Проверяем несколько точек вдоль стены.
                // ----------------------------------------------------

                bool intersectsWall =
                    false;

                double[] positions =
                {
                    0.0,
                    wall.Length * 0.25,
                    wall.Length * 0.50,
                    wall.Length * 0.75,
                    wall.Length
                };

                foreach (double position in positions)
                {
                    XYZ point =
                        wall.StartPoint
                        + wall.Direction * position;

                    if (
                        point.X >= box.Min.X - xyTolerance &&
                        point.X <= box.Max.X + xyTolerance &&
                        point.Y >= box.Min.Y - xyTolerance &&
                        point.Y <= box.Max.Y + xyTolerance)
                    {
                        intersectsWall =
                            true;

                        break;
                    }
                }

                if (!intersectsWall)
                {
                    continue;
                }

                // ----------------------------------------------------
                // Если найдено несколько плит,
                // берём ближайшую к верху стены.
                // ----------------------------------------------------

                if (zDistance < bestDistance)
                {
                    bestDistance =
                        zDistance;

                    best =
                        new SlabData
                        {
                            Floor = floor,
                            BottomZ = slabBottom,
                            TopZ = slabTop,
                            Thickness =
                                slabTop - slabBottom
                        };
                }
            }

            return best;
        }


        // ============================================================
        // ОПРЕДЕЛЯЕМ КОНЕЧНУЮ ОТМЕТКУ
        // ВЕРТИКАЛЬНОЙ АРМАТУРЫ
        // ============================================================

        private static double GetFinalVerticalBarTop(
            Document doc,
            WallData wall)
        {
            SlabData slab =
                FindSlabAboveWall(
                    doc,
                    wall);

            // --------------------------------------------------------
            // ПЛИТЫ НЕТ
            //
            // Оставляем старое правило:
            //
            // 20 мм от верха стены.
            // --------------------------------------------------------

            if (slab == null)
            {
                return
                    wall.TopZ
                    - ToFeet(
                        FinalTopOffsetMm);
            }

            // --------------------------------------------------------
            // ПЛИТА ЕСТЬ
            //
            // Заводим вертикальную арматуру
            // почти до верха плиты.
            //
            // 20 мм до верхней поверхности.
            // --------------------------------------------------------

            return
                slab.TopZ
                - ToFeet(
                    FinalTopOffsetMm);
        }


        // ============================================================
        // ОТМЕТКА П-ШКИ НА СВОБОДНОМ ВЕРХУ
        // ============================================================

        private static double GetTopPBarConcreteTop(
            Document doc,
            WallData wall)
        {
            SlabData slab =
                FindSlabAboveWall(
                    doc,
                    wall);

            if (slab != null)
            {
                return slab.TopZ;
            }

            return wall.TopZ;
        }
    }


    // ================================================================
    // DATA
    // ================================================================

    public class SlabData
    {
        public Floor Floor { get; set; }

        public double BottomZ { get; set; }

        public double TopZ { get; set; }

        public double Thickness { get; set; }
    }
}