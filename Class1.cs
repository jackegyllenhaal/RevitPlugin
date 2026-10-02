
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
    [Transaction(TransactionMode.Manual)]
    public partial class HelloRevit : IExternalCommand
    {
        // ============================================================
        // НАСТРОЙКИ
        // ============================================================

        private const double VerticalSpacingMm = 300.0;
        private const double HorizontalSpacingMm = 300.0;

        private const double VerticalAxisOffsetMm = 50.0;
        private const double VerticalEndOffsetMm = 50.0;

        private const double HorizontalConcreteEdgeOffsetMm = 20.0;

        private const double OpeningVerticalOffsetMm = 50.0;
        private const double OpeningExtraThresholdMm = 100.0;

        private const double FinalTopOffsetMm = 20.0;
        private const double OpeningVerticalGapMm = 20.0;

        private const double ZToleranceMm = 5.0;

        private const double OpeningScanStepMm = 50.0;
        private const double OpeningRefineToleranceMm = 1.0;


        // ============================================================
        // EXECUTE
        // ============================================================

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uidoc =
                commandData.Application.ActiveUIDocument;

            Document doc =
                uidoc.Document;

            try
            {
                // ====================================================
                // 1. ВЫБИРАЕМ СБОРКУ
                // ====================================================

                Reference reference =
                    uidoc.Selection.PickObject(
                        ObjectType.Element,
                        "Выберите сборку стены");

                Element selected =
                    doc.GetElement(
                        reference.ElementId);

                AssemblyInstance assembly =
                    selected as AssemblyInstance;

                if (assembly == null)
                {
                    TaskDialog.Show(
                        "Армирование стены",
                        "Выбранный элемент не является сборкой.");

                    return Result.Cancelled;
                }


                // ====================================================
                // 2. ЧИТАЕМ СТЕНЫ
                // ====================================================

                List<WallData> walls =
                    ReadWalls(
                        doc,
                        assembly);

                if (walls.Count == 0)
                {
                    TaskDialog.Show(
                        "Армирование стены",
                        "В выбранной сборке стены не найдены.");

                    return Result.Cancelled;
                }

                walls =
                    walls
                        .OrderBy(x => x.BottomZ)
                        .ToList();


                // ====================================================
                // ДИАГНОСТИКА Г-ОБРАЗНЫХ УЗЛОВ
                // ====================================================

                string cornerReport =
                    BuildCornerJointReport(
                        walls);

                TaskDialog.Show(
                    "Диагностика Г-узлов",
                    cornerReport);


                // ====================================================
                // ДИАГНОСТИКА Т-ОБРАЗНЫХ УЗЛОВ
                // ====================================================

                string tJointReport =
                    BuildTJointReport(
                        walls);

                TaskDialog.Show(
                    "Диагностика Т-узлов",
                    tJointReport);


                // ====================================================
                // 3. ВЕРТИКАЛЬНЫЕ ВЕТВИ
                // ====================================================

                List<List<WallData>> verticalBranches =
                    BuildVerticalWallBranches(
                        walls);


                // ====================================================
                // 4. ВЕРТИКАЛЬНАЯ Ø10
                // ====================================================

                RebarBarType verticalBarType =
                    FindWallBarType(
                        doc,
                        10.0,
                        "стена_верт");

                if (verticalBarType == null)
                {
                    TaskDialog.Show(
                        "Ошибка",
                        "Не найден тип вертикальной арматуры Ø10.");

                    return Result.Cancelled;
                }


                // ====================================================
                // 4A. УЗЛОВАЯ ВЕРТИКАЛЬНАЯ Ø12
                // ====================================================

                RebarBarType cornerVerticalBarType =
                    FindJointVerticalBarType(
                        doc,
                        12.0,
                        "стена_верт");

                if (cornerVerticalBarType == null)
                {
                    TaskDialog.Show(
                        "Ошибка",
                        "Не найден тип узловой вертикальной арматуры Ø12 или больше.\n" +
                        "Ожидается тип с диаметром не менее 12 мм и именем, " +
                        "содержащим 'стена_верт'.");

                    return Result.Cancelled;
                }


                // ====================================================
                // 5. ГОРИЗОНТАЛЬНАЯ Ø10
                // ====================================================

                RebarBarType horizontalBarType =
                    FindWallBarType(
                        doc,
                        10.0,
                        "стена_гор");

                if (horizontalBarType == null)
                {
                    TaskDialog.Show(
                        "Ошибка",
                        "Не найден тип горизонтальной арматуры Ø10.");

                    return Result.Cancelled;
                }


                // ====================================================
                // 6. ШПИЛЬКА Ø6 A240
                // ====================================================

                RebarBarType hairpinBarType =
                    FindHairpinBarType(
                        doc);

                if (hairpinBarType == null)
                {
                    TaskDialog.Show(
                        "Ошибка",
                        "Не найден тип:\n" +
                        "(арматура)детали_d=6_A240");

                    return Result.Cancelled;
                }

                RebarShape hairpinShape =
                    FindRebarShapeExact(
                        doc,
                        "(форма)шпилька");

                if (hairpinShape == null)
                {
                    TaskDialog.Show(
                        "Ошибка",
                        "Не найдена форма:\n" +
                        "(форма)шпилька");

                    return Result.Cancelled;
                }


                // ====================================================
                // 7. ДИАМЕТРЫ
                // ====================================================

                double verticalDiameterFeet =
                    GetBarDiameterFeet(
                        verticalBarType);

                double horizontalDiameterFeet =
                    GetBarDiameterFeet(
                        horizontalBarType);


                // ====================================================
                // 8. ВЕРТИКАЛЬНАЯ СЕТКА
                // ====================================================

                double verticalAxisOffset =
                    ToFeet(
                        VerticalAxisOffsetMm);


                // ====================================================
                // 9. ГОРИЗОНТАЛЬНАЯ СЕТКА
                //
                // 50 - 5 - 5 = 40 мм
                // ====================================================

                double horizontalAxisOffset =
                    verticalAxisOffset
                    - verticalDiameterFeet / 2.0
                    - horizontalDiameterFeet / 2.0;

                if (horizontalAxisOffset <= 0)
                {
                    throw new Exception(
                        "Ошибка расчёта положения " +
                        "горизонтальной арматуры.");
                }


                // ====================================================
                // 10. БЕТОН
                // ====================================================

                ConcreteInfo concrete =
                    FindConcreteClass(
                        doc,
                        walls);

                if (concrete == null)
                {
                    TaskDialog.Show(
                        "Ошибка",
                        "Не удалось определить класс бетона.");

                    return Result.Cancelled;
                }

                double verticalDiameterMm =
                    ToMm(
                        verticalDiameterFeet);

                double lapMm =
                    concrete.LapFactor
                    * verticalDiameterMm;


                // ====================================================
                // 11. ПРОЁМЫ
                // ====================================================

                foreach (WallData wall in walls)
                {
                    wall.Openings =
                        DetectOpenings(
                            wall);
                }


                // ====================================================
                // Г-ОБРАЗНЫЕ УЗЛЫ
                // ====================================================

                ApplyCornerHorizontalExtensions(
                    walls);


                // ====================================================
                // Т-ОБРАЗНЫЕ УЗЛЫ
                //
                // В WallJointService.cs этот метод называется
                // ApplyTHorizontalExtensions.
                // ====================================================

                ApplyTHorizontalExtensions(
                    walls);


                // ====================================================
                // 12. СЧЁТЧИКИ
                // ====================================================

                int verticalMainCount = 0;

                int cornerVertical12Count = 0;

                int tJointVertical12Count = 0;

                int crossVertical12Count = 0;

                int freeEndVertical12Count = 0;

                int horizontalCount = 0;

                int openingExtraCount = 0;


                // ШПИЛЬКИ

                int hairpinCount = 0;

                int hairpinPositionCount = 0;

                int hairpinLevelCount = 0;

                int hairpinCheckerboardNodes = 0;

                int hairpinSkippedAtEnds = 0;

                int hairpinSkippedByOpening = 0;

                int hairpinCreateAttempts = 0;


                // П-ШКИ

                int horizontalPBarCount = 0;

                int openingTopPBarCount = 0;

                int topPBarCount = 0;

                int topCornerPBarCount = 0;


                // ====================================================
                // 13. СОЗДАЁМ АРМАТУРУ
                // ====================================================

                using (Transaction transaction =
                    new Transaction(
                        doc,
                        "Армирование стены"))
                {
                    transaction.Start();


                    // =================================================
                    // 14. ОСНОВНАЯ ВЕРТИКАЛЬНАЯ Ø10
                    // =================================================

                    foreach (
                        List<WallData> branch
                        in verticalBranches)
                    {
                        verticalMainCount +=
                            CreateVerticalMainBars(
                                doc,
                                branch,
                                walls,
                                verticalBarType,
                                lapMm);
                    }


                    // =================================================
                    // 14A. 4Ø12 В Г-ОБРАЗНЫХ УЗЛАХ
                    // =================================================

                    cornerVertical12Count =
                        CreateCornerVerticalBars(
                            doc,
                            walls,
                            cornerVerticalBarType,
                            concrete.LapFactor * 10.0);


                    // =================================================
                    // 14B. 4Ø12 В Т-ОБРАЗНЫХ УЗЛАХ
                    //
                    // Оси по 50 мм от бетонных граней.
                    // =================================================

                    tJointVertical12Count =
                        CreateTJointVerticalBars(
                            doc,
                            walls,
                            cornerVerticalBarType,
                            concrete.LapFactor * 10.0);


                    // =================================================
                    // 14B-1. 4Ø12 В КРЕСТООБРАЗНЫХ (+) УЗЛАХ
                    // =================================================

                    crossVertical12Count =
                        CreateCrossVerticalBars(
                            doc,
                            walls,
                            cornerVerticalBarType,
                            concrete.LapFactor * 10.0);


                    // =================================================
                    // 14C. 6Ø12 НА СВОБОДНЫХ ТОРЦАХ ЛЮБЫХ СТЕН
                    // =================================================

                    freeEndVertical12Count =
                        CreateFreeEndVertical12Bars(
                            doc,
                            walls,
                            cornerVerticalBarType,
                            concrete.LapFactor * 10.0);


                    // =================================================
                    // 15. ГОРИЗОНТАЛЬНАЯ Ø10
                    // =================================================

                    foreach (WallData wall in walls)
                    {
                        horizontalCount +=
                            CreateHorizontalBars(
                                doc,
                                wall,
                                walls,
                                horizontalBarType,
                                horizontalAxisOffset);
                    }

                    // В кресте заменяем две пары половинок
                    // двумя физическими сплошными Ø10 на каждом уровне.
                    horizontalCount +=
                        CreateCrossHorizontalBars(
                            doc,
                            walls,
                            horizontalBarType,
                            horizontalAxisOffset);


                    // =================================================
                    // 16. ДОП. ВЕРТИКАЛЬНАЯ У ПРОЁМОВ
                    // =================================================

                    foreach (WallData wall in walls)
                    {
                        openingExtraCount +=
                            CreateOpeningExtraVerticalBars(
                                doc,
                                wall,
                                verticalBarType);
                    }


                    // =================================================
                    // 17. ШПИЛЬКИ Ø6
                    // =================================================

                    foreach (WallData wall in walls)
                    {
                        int positionCount;
                        int levelCount;
                        int checkerboardNodes;
                        int skippedAtEnds;
                        int skippedByOpening;
                        int createAttempts;

                        hairpinCount +=
                            CreateHairpinsCheckerboard(
                                doc,
                                wall,
                                hairpinShape,
                                hairpinBarType,
                                horizontalAxisOffset,
                                walls,
                                out positionCount,
                                out levelCount,
                                out checkerboardNodes,
                                out skippedAtEnds,
                                out skippedByOpening,
                                out createAttempts);

                        hairpinPositionCount +=
                            positionCount;

                        hairpinLevelCount +=
                            levelCount;

                        hairpinCheckerboardNodes +=
                            checkerboardNodes;

                        hairpinSkippedAtEnds +=
                            skippedAtEnds;

                        hairpinSkippedByOpening +=
                            skippedByOpening;

                        hairpinCreateAttempts +=
                            createAttempts;
                    }


                    // =================================================
                    // 18. ГОРИЗОНТАЛЬНЫЕ П-ШКИ
                    // =================================================

                    foreach (WallData wall in walls)
                    {
                        horizontalPBarCount +=
                            CreateTestPBars(
                                doc,
                                wall,
                                walls,
                                horizontalAxisOffset);
                    }


                    // =================================================
                    // 19. ВЕРТИКАЛЬНЫЕ П-ШКИ НАД ПРОЁМАМИ
                    // =================================================

                    foreach (WallData wall in walls)
                    {
                        openingTopPBarCount +=
                            CreateOpeningTopPBars(
                                doc,
                                wall,
                                horizontalAxisOffset);
                    }


                    // =================================================
                    // 20. ВЕРХНИЕ ВЕРТИКАЛЬНЫЕ П-ШКИ
                    //     ТОЛЬКО НА ПОСЛЕДНЕМ ЭТАЖЕ
                    // =================================================
                    // Для каждой вертикальной ветви берём только самый
                    // верхний участок стены. На промежуточных этажах
                    // П-шки не создаются.
                    // =================================================

                    foreach (List<WallData> branch in verticalBranches)
                    {
                        if (branch == null || branch.Count == 0)
                        {
                            continue;
                        }

                        WallData topWall =
                            branch
                                .OrderByDescending(x => x.TopZ)
                                .First();

                        topPBarCount +=
                            CreateTopVerticalPBars(
                                doc,
                                topWall,
                                walls,
                                horizontalAxisOffset);
                    }


                    // =================================================
                    // 20A. ВЕРХНИЕ П-ШКИ Г-ОБРАЗНЫХ УЗЛОВ 4Ø12
                    // =================================================

                    topCornerPBarCount =
                        CreateTopCornerPBars(
                            doc,
                            walls);


                    // =================================================
                    // 21. ОБНОВЛЯЕМ ДОКУМЕНТ
                    // =================================================

                    doc.Regenerate();


                    // =================================================
                    // 22. COMMIT
                    // =================================================

                    if (transaction.Commit() != TransactionStatus.Committed)
                    {
                        message = "Revit не подтвердил сохранение арматуры.";
                        return Result.Failed;
                    }
                }


                // ====================================================
                // 22A. ДИАГНОСТИКА ВОЗМОЖНЫХ ДУБЛЕЙ АРМАТУРЫ
                //
                // Только отчёт. Ничего не удаляем и не перемещаем.
                // Проверка запускается ПОСЛЕ Commit(), поэтому не влияет
                // на создание арматуры.
                // ====================================================

                string duplicateRebarReport;
                try
                {
                    duplicateRebarReport = BuildDuplicateRebarReport(doc, assembly);
                }
                catch (Exception ex)
                {
                    // Арматура уже сохранена: ошибка диагностики не означает откат.
                    duplicateRebarReport = "Арматура сохранена. Проверка дублей не завершена: " + ex.Message;
                }


                // ====================================================
                // 23. ОТЧЁТ
                // ====================================================

                StringBuilder result =
                    new StringBuilder();

                result.AppendLine(
                    "АРМИРОВАНИЕ СТЕНЫ");

                result.AppendLine(
                    "============================");

                result.AppendLine();


                // ====================================================
                // СБОРКА
                // ====================================================

                result.AppendLine(
                    $"Сборка: {assembly.Name}");

                result.AppendLine(
                    $"Участков стены: {walls.Count}");

                result.AppendLine();


                // ====================================================
                // ТИПЫ АРМАТУРЫ
                // ====================================================

                result.AppendLine(
                    $"Вертикальная: " +
                    $"{verticalBarType.Name}");

                result.AppendLine(
                    $"Горизонтальная: " +
                    $"{horizontalBarType.Name}");

                result.AppendLine();


                // ====================================================
                // БЕТОН
                // ====================================================

                result.AppendLine(
                    $"Бетон: {concrete.Name}");

                result.AppendLine(
                    $"Нахлёст: " +
                    $"{concrete.LapFactor:F0}d = " +
                    $"{lapMm:F0} мм");

                result.AppendLine();


                // ====================================================
                // ПОЛОЖЕНИЕ СЕТОК
                // ====================================================

                result.AppendLine(
                    $"Ось вертикальной: " +
                    $"{VerticalAxisOffsetMm:F0} мм");

                result.AppendLine(
                    $"Ось горизонтальной: " +
                    $"{ToMm(horizontalAxisOffset):F0} мм");

                result.AppendLine();


                // ====================================================
                // ОСНОВНАЯ АРМАТУРА
                // ====================================================

                result.AppendLine(
                    "ОСНОВНАЯ АРМАТУРА");

                result.AppendLine(
                    "============================");

                result.AppendLine(
                    $"Вертикальных Ø10 создано: " +
                    $"{verticalMainCount}");

                result.AppendLine(
                    $"Вертикальных Ø12 Г-узлов создано: " +
                    $"{cornerVertical12Count}");

                result.AppendLine(
                    $"Вертикальных Ø12 Т-узлов создано: " +
                    $"{tJointVertical12Count}");

                result.AppendLine(
                    $"Вертикальных Ø12 +-узлов создано: " +
                    $"{crossVertical12Count}");

                result.AppendLine(
                    $"Вертикальных Ø12 свободных торцов: " +
                    $"{freeEndVertical12Count}");

                result.AppendLine(
                    $"Всего Ø12 создано: " +
                    $"{cornerVertical12Count + tJointVertical12Count + crossVertical12Count + freeEndVertical12Count}");

                result.AppendLine(
                    $"Горизонтальных Ø10 создано: " +
                    $"{horizontalCount}");

                result.AppendLine(
                    $"Доп. вертикальных у проёмов: " +
                    $"{openingExtraCount}");

                result.AppendLine();


                // ====================================================
                // ШПИЛЬКИ
                // ====================================================

                result.AppendLine(
                    "ШПИЛЬКИ Ø6");

                result.AppendLine(
                    "============================");

                result.AppendLine(
                    $"Тип: {hairpinBarType.Name}");

                result.AppendLine(
                    $"Вертикальных позиций: " +
                    $"{hairpinPositionCount}");

                result.AppendLine(
                    $"Горизонтальных уровней: " +
                    $"{hairpinLevelCount}");

                result.AppendLine(
                    $"Узлов шахматки: " +
                    $"{hairpinCheckerboardNodes}");

                result.AppendLine(
                    $"Пропущено у торцов: " +
                    $"{hairpinSkippedAtEnds}");

                result.AppendLine(
                    $"Пропущено из-за проёмов: " +
                    $"{hairpinSkippedByOpening}");

                result.AppendLine(
                    $"Попыток создания: " +
                    $"{hairpinCreateAttempts}");

                result.AppendLine(
                    $"Создано шпилек: " +
                    $"{hairpinCount}");

                result.AppendLine();


                // ====================================================
                // ГОРИЗОНТАЛЬНЫЕ П-ШКИ
                // ====================================================

                result.AppendLine(
                    "П-ШКИ ГОРИЗОНТАЛЬНОЙ АРМАТУРЫ");

                result.AppendLine(
                    "============================");

                result.AppendLine(
                    $"Создано: " +
                    $"{horizontalPBarCount}");

                result.AppendLine(
                    "Ножки: 500 мм");

                result.AppendLine(
                    "Учитываются края стены, " +
                    "проёмы, Г-образные и Т-образные узлы.");

                result.AppendLine();


                // ====================================================
                // П-ШКИ НАД ПРОЁМАМИ
                // ====================================================

                result.AppendLine(
                    "П-ШКИ НАД ПРОЁМАМИ");

                result.AppendLine(
                    "============================");

                result.AppendLine(
                    $"Создано: " +
                    $"{openingTopPBarCount}");

                result.AppendLine(
                    "Ножки: 500 мм вверх");

                result.AppendLine(
                    "Положение: над верхней гранью проёма.");

                result.AppendLine();


                // ====================================================
                // ВЕРХНИЕ П-ШКИ
                // ====================================================

                result.AppendLine(
                    "П-ШКИ НА СВОБОДНОМ ВЕРХНЕМ КРАЕ");

                result.AppendLine(
                    "============================");

                result.AppendLine(
                    $"Обычных создано: " +
                    $"{topPBarCount}");

                result.AppendLine(
                    $"Угловых Г-узла создано: " +
                    $"{topCornerPBarCount}");

                result.AppendLine(
                    $"Всего верхних П-шек: " +
                    $"{topPBarCount + topCornerPBarCount}");

                result.AppendLine(
                    "Ножки: 500 мм вниз");

                result.AppendLine(
                    "Защитный слой сверху: 20 мм");

                result.AppendLine(
                    "Шаг обычных П-шек соответствует " +
                    "вертикальной арматуре.");


                result.AppendLine();
                result.AppendLine();
                result.AppendLine(
                    "ПРОВЕРКА ДУБЛЕЙ");

                result.AppendLine(
                    "============================");

                result.AppendLine(
                    duplicateRebarReport);


                // ====================================================
                // 24. ПОКАЗЫВАЕМ РЕЗУЛЬТАТ
                // ====================================================

                TaskDialog.Show(
                    "Армирование стены",
                    result.ToString());

                return Result.Succeeded;
            }


            // ========================================================
            // ОТМЕНА ВЫБОРА
            // ========================================================

            catch (
                Autodesk.Revit.Exceptions
                    .OperationCanceledException)
            {
                return Result.Cancelled;
            }


            // ========================================================
            // ОШИБКА
            // ========================================================

            catch (Exception ex)
            {
                message =
                    ex.ToString();

                TaskDialog.Show(
                    "Ошибка",
                    ex.ToString());

                return Result.Failed;
            }
        }


        // ============================================================
        // УЗЛОВАЯ ВЕРТИКАЛЬНАЯ АРМАТУРА
        //
        // Для Г-, Т- и крестообразных узлов:
        //     минимум Ø12.
        //
        // Приоритет:
        //     Ø12 -> Ø14 -> Ø16 -> ...
        //
        // Ø10 и меньшие диаметры сюда никогда не попадают.
        // ============================================================

        private static RebarBarType FindJointVerticalBarType(
            Document doc,
            double minimumDiameterMm,
            string namePart)
        {
            const double diameterToleranceMm = 0.5;

            return new FilteredElementCollector(doc)
                .OfClass(typeof(RebarBarType))
                .Cast<RebarBarType>()
                .Select(x => new
                {
                    BarType = x,
                    DiameterMm = ToMm(GetBarDiameterFeet(x))
                })
                .Where(x =>
                    x.BarType != null &&
                    x.BarType.Name.IndexOf(
                        namePart,
                        StringComparison.OrdinalIgnoreCase) >= 0 &&
                    x.DiameterMm >=
                        minimumDiameterMm - diameterToleranceMm)
                .OrderBy(x => x.DiameterMm)
                .Select(x => x.BarType)
                .FirstOrDefault();
        }


        // ============================================================
        // ОСТАЛЬНЫЕ МЕТОДЫ:
        //
        // WallData.cs
        // RebarService.cs
        // HairpinService.cs
        // PBarService.cs
        // WallJointService.cs
        // CornerVerticalRebarService.cs
        // CornerPBarService.cs
        // TJointVerticalRebarService.cs
        //
        // ============================================================
    }
}
