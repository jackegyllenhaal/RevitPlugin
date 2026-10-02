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

        // ============================================================
        // ФАКТИЧЕСКИЕ ВЕРТИКАЛЬНЫЕ ПОЗИЦИИ ДЛЯ ШПИЛЕК
        //
        // Шпилька создаётся только в пересечении:
        // фактический вертикальный стержень × горизонтальный стержень.
        //
        // Обычные Ø10, удалённые из зоны свободного торца, исключаем.
        // Специальные Ø12 добавляем на 50 / 200 / 350 мм.
        // ============================================================

        private static List<double> GetActualVerticalPositionsForHairpins(
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

            foreach (double position in source)
            {
                if (IsMainVerticalPositionBlockedByFreeEnd12(
                        doc,
                        wall,
                        allWalls,
                        position))
                {
                    continue;
                }

                AddUniqueHairpinVerticalPosition(
                    result,
                    position);
            }

            // Специальные позиции 50/200/350 относятся только к 6Ø12
            // свободного торца СЖм-/СШм-.
            bool hasSpecialFreeEnd12 =
                IsStiffnessOrShaftWall(doc, wall);

            double[] offsetsMm =
            {
                50.0,
                200.0,
                350.0
            };

            if (hasSpecialFreeEnd12 &&
                IsWallEndFree(
                    wall,
                    WallEndSide.Start,
                    allWalls))
            {
                foreach (double offsetMm in offsetsMm)
                {
                    double position = ToFeet(offsetMm);

                    if (position > 0.0 &&
                        position < wall.Length)
                    {
                        AddUniqueHairpinVerticalPosition(
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
                        wall.Length - ToFeet(offsetMm);

                    if (position > 0.0 &&
                        position < wall.Length)
                    {
                        AddUniqueHairpinVerticalPosition(
                            result,
                            position);
                    }
                }
            }

            result.Sort();
            return result;
        }


        private static void AddUniqueHairpinVerticalPosition(
            List<double> positions,
            double position)
        {
            double tolerance = ToFeet(1.0);

            foreach (double existing in positions)
            {
                if (Math.Abs(existing - position) <= tolerance)
                    return;
            }

            positions.Add(position);
        }


        private static int CreateHairpinsCheckerboard(

            Document doc,

            WallData wall,

            RebarShape hairpinShape,

            RebarBarType barType,

            double horizontalAxisOffset,

            List<WallData> allWalls,

            out int positionCount,

            out int levelCount,

            out int checkerboardNodes,

            out int skippedAtEnds,

            out int skippedByOpening,

            out int createAttempts)

        {

            int count = 0;



            positionCount = 0;

            levelCount = 0;

            checkerboardNodes = 0;

            skippedAtEnds = 0;

            skippedByOpening = 0;

            createAttempts = 0;



            List<double> positions =
                GetActualVerticalPositionsForHairpins(
                    doc,
                    wall,
                    allWalls);



            List<double> levels =

                GetHorizontalLevels(wall);



            positionCount = positions.Count;

            levelCount = levels.Count;



            if (positions.Count == 0 ||

                levels.Count == 0)

            {

                return count;

            }



            double requiredWidth =

                wall.Thickness

                - 2.0 * horizontalAxisOffset;



            if (requiredWidth <= ToFeet(10.0))

            {

                throw new Exception(

                    "Не удалось рассчитать ширину шпильки.");

            }



            // Размер формы вдоль стены.

            double requiredDepth =

                ToFeet(115.0);



            double endClearance =

                requiredDepth / 2.0

                + ToFeet(HorizontalConcreteEdgeOffsetMm);



            // ========================================================

            // ШАХМАТНАЯ РАСКЛАДКА

            // ========================================================



            for (int rowIndex = 0;

                 rowIndex < levels.Count;

                 rowIndex++)

            {

                double z =

                    levels[rowIndex];



                for (int positionIndex = 0;

                     positionIndex < positions.Count;

                     positionIndex++)

                {

                    // Шахматка по пересечениям основной сетки.

                    //

                    // Ряд 0: 0, 2, 4...

                    // Ряд 1: 1, 3, 5...

                    // Ряд 2: 0, 2, 4...



                    if ((rowIndex + positionIndex) % 2 != 0)

                    {

                        continue;

                    }



                    double position =

                        positions[positionIndex];



                    checkerboardNodes++;



                    // ------------------------------------------------

                    // НЕ СТАВИМ У ТОРЦОВ

                    // ------------------------------------------------



                    if (position < endClearance ||

                        position > wall.Length - endClearance)

                    {

                        skippedAtEnds++;

                        continue;

                    }



                    // ------------------------------------------------

                    // НЕ СТАВИМ В ПРОЁМАХ

                    // ------------------------------------------------



                    if (!HasMainGridAt(

                        wall,

                        position,

                        z))

                    {

                        skippedByOpening++;

                        continue;

                    }



                    // =================================================

                    // РАСЧЁТНЫЙ УЗЕЛ СЕТКИ

                    // =================================================



                    // Свободный торец СЖм-/СШм-: внутри 500 мм ножек



                    // торцевой горизонтальной П-шки шпильку Ø6 не создаём.



                    if (IsHairpinInsideFreeEndPBarZone(doc, wall, allWalls, position))



                    {



                        continue;



                    }




                    XYZ center =

                        wall.StartPoint

                        + wall.Direction * position;



                    center =

                        new XYZ(

                            center.X,

                            center.Y,

                            z);

                    // Внутри зоны креста (+) шпилек Ø6 не создаём.
                    if (IsPointInsideCrossJointZone(
                        wall,
                        center,
                        allWalls))
                    {
                        continue;
                    }



                    // =================================================

                    // BOX ДЛЯ ШПИЛЬКИ

                    //

                    // ВАЖНО:

                    // шпильку поднимаем ТОЛЬКО вверх на 8 мм.

                    //

                    // По длине стены положение не меняется.

                    // По толщине стены положение не меняется.

                    // =================================================



                    double verticalShift =

                        ToFeet(8.0);



                    XYZ boxOrigin =

                        center

                        + XYZ.BasisZ * verticalShift

                        - wall.Normal * (requiredWidth / 2.0)

                        - wall.Direction * (requiredDepth / 2.0);



                    XYZ xVector =

                        wall.Normal * requiredWidth;



                    XYZ yVector =

                        wall.Direction * requiredDepth;



                    try

                    {

                        createAttempts++;



                        Rebar hairpin =

                            Rebar.CreateFromRebarShape(

                                doc,

                                hairpinShape,

                                barType,

                                wall.Wall,

                                boxOrigin,

                                wall.Normal,

                                wall.Direction);



                        if (hairpin == null)

                        {

                            continue;

                        }



                        doc.Regenerate();



                        RebarShapeDrivenAccessor accessor =

                            hairpin.GetShapeDrivenAccessor();



                        if (accessor == null)

                        {

                            doc.Delete(hairpin.Id);

                            continue;

                        }



                        accessor.ScaleToBox(
     boxOrigin,
     xVector,
     yVector);

 doc.Regenerate();

 // ------------------------------------------------
 // ВЫРАВНИВАНИЕ ПО ВЕРТИКАЛЬНОМУ Ø10
 //
 // Высоту не меняем. Корректируем только положение
 // вдоль стены, чтобы геометрический центр шпильки
 // совпал с расчётной позицией вертикального стержня.
 // ------------------------------------------------

 BoundingBoxXYZ hairpinBox =
     hairpin.get_BoundingBox(null);

 if (hairpinBox != null)
 {
     XYZ actualCenter =
         (hairpinBox.Min + hairpinBox.Max) / 2.0;

     XYZ delta =
         center - actualCenter;

     double alongWall =
         delta.DotProduct(wall.Direction);

     XYZ horizontalMove =
         wall.Direction * alongWall;

     if (horizontalMove.GetLength() > ToFeet(0.1))
     {
         ElementTransformUtils.MoveElement(
             doc,
             hairpin.Id,
             horizontalMove);

         doc.Regenerate();
     }
 }

 count++;

                    }

                    catch (Exception ex)

                    {

                        throw new Exception(

                            "Ошибка создания шпилек.\n\n" +

                            "Форма: " + hairpinShape.Name + "\n" +

                            "Тип: " + barType.Name + "\n" +

                            "Стена ID: " +

                            wall.Wall.Id.IntegerValue + "\n" +

                            "Положение: " +

                            ToMm(position).ToString("F0") + " мм\n" +

                            "Z: " +

                            ToMm(z).ToString("F0") + " мм\n\n" +

                            "Ошибка Revit:\n" +

                            ex.Message);

                    }

                }

            }



            doc.Regenerate();



            return count;

        }





        // ============================================================

        // ЕСТЬ ЛИ ОСНОВНАЯ СЕТКА В ЭТОМ УЗЛЕ

        // ============================================================
        private static bool IsHairpinInsideFreeEndPBarZone(
            Document doc,
            WallData wall,
            List<WallData> allWalls,
            double position)
        {
            if (wall == null || wall.Wall == null || allWalls == null)
                return false;

            if (doc == null)
                doc = wall.Wall.Document;

            // Только СЖм-/СШм-.
            if (!IsStiffnessOrShaftWall(doc, wall))
                return false;

            double zone = ToFeet(500.0);
            double tolerance = ToFeet(2.0);

            if (IsWallEndFree(wall, WallEndSide.Start, allWalls) &&
                position <= zone + tolerance)
                return true;

            if (IsWallEndFree(wall, WallEndSide.End, allWalls) &&
                position >= wall.Length - zone - tolerance)
                return true;

            return false;
        }





        private static bool HasMainGridAt(

            WallData wall,

            double position,

            double z)

        {

            if (wall.Openings == null)

            {

                return true;

            }



            foreach (OpeningData opening in wall.Openings)

            {

                bool positionInside =

                    position > opening.Left &&

                    position < opening.Right;



                bool zInside =

                    z > opening.BottomZ &&

                    z < opening.TopZ;



                if (positionInside && zInside)

                {

                    return false;

                }

            }



            return true;

        }





        // ============================================================

        // ПОИСК ФОРМЫ ШПИЛЬКИ

        // ============================================================



        private static RebarShape FindRebarShapeExact(

            Document doc,

            string exactName)

        {

            List<RebarShape> shapes =

                new FilteredElementCollector(doc)

                .OfClass(typeof(RebarShape))

                .Cast<RebarShape>()

                .ToList();



            foreach (RebarShape shape in shapes)

            {

                if (string.Equals(

                    shape.Name.Trim(),

                    exactName.Trim(),

                    StringComparison.OrdinalIgnoreCase))

                {

                    return shape;

                }

            }



            return null;

        }





        // ============================================================

        // ПОИСК Ø6 A240

        // ============================================================



        private static RebarBarType FindHairpinBarType(

            Document doc)

        {

            List<RebarBarType> types =

                new FilteredElementCollector(doc)

                .OfClass(typeof(RebarBarType))

                .Cast<RebarBarType>()

                .ToList();



            // Сначала ищем ожидаемый тип:

            // (арматура)детали_d=6_A240



            foreach (RebarBarType type in types)

            {

                double diameterMm =

                    ToMm(

                        GetBarDiameterFeet(type));



                if (Math.Abs(

                    diameterMm - 6.0) > 0.5)

                {

                    continue;

                }



                bool details =

                    type.Name.IndexOf(

                        "детали",

                        StringComparison.OrdinalIgnoreCase)

                    >= 0;



                bool a240 =

                    type.Name.IndexOf(

                        "A240",

                        StringComparison.OrdinalIgnoreCase)

                    >= 0;



                if (details && a240)

                {

                    return type;

                }

            }



            // --------------------------------------------------------

            // РЕЗЕРВНЫЙ ПОИСК ЧЕРЕЗ ПАРАМЕТР КЛАССА

            // --------------------------------------------------------



            foreach (RebarBarType type in types)

            {

                double diameterMm =

                    ToMm(

                        GetBarDiameterFeet(type));



                if (Math.Abs(

                    diameterMm - 6.0) > 0.5)

                {

                    continue;

                }



                if (type.Name.IndexOf(

                    "детали",

                    StringComparison.OrdinalIgnoreCase)

                    < 0)

                {

                    continue;

                }



                foreach (Parameter p in type.Parameters)

                {

                    if (p == null ||

                        p.Definition == null ||

                        !p.HasValue)

                    {

                        continue;

                    }



                    if (p.Definition.Name.IndexOf(

                        "класс_арматуры",

                        StringComparison.OrdinalIgnoreCase)

                        < 0)

                    {

                        continue;

                    }



                    string value =

                        GetParameterText(p);



                    if (value.IndexOf(

                        "240",

                        StringComparison.OrdinalIgnoreCase)

                        >= 0)

                    {

                        return type;

                    }

                }

            }



            return null;

        }

    }

}
