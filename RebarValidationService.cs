using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RevitPlugin
{
    public partial class HelloRevit
    {
        // ============================================================
        // ДИАГНОСТИКА ВОЗМОЖНЫХ ДУБЛЕЙ АРМАТУРЫ
        //
        // ВАЖНО:
        // - ничего не удаляет;
        // - ничего не перемещает;
        // - проверяет только Rebar, относящиеся к стенам выбранной сборки;
        // - сравнивает диаметр + геометрию центральных кривых;
        // - допуск геометрии = 2 мм.
        // ============================================================

        private static string BuildDuplicateRebarReport(
            Document doc,
            AssemblyInstance assembly)
        {
            if (doc == null || assembly == null)
            {
                return "Диагностика не выполнена: нет документа или сборки.";
            }

            HashSet<ElementId> wallIds =
                new HashSet<ElementId>(
                    assembly.GetMemberIds()
                        .Where(id => doc.GetElement(id) is Wall));

            if (wallIds.Count == 0)
            {
                return "Диагностика не выполнена: в сборке нет стен.";
            }

            List<RebarSnapshot> rebars =
                new List<RebarSnapshot>();

            foreach (Rebar rebar in
                new FilteredElementCollector(doc)
                    .OfClass(typeof(Rebar))
                    .Cast<Rebar>())
            {
                ElementId hostId =
                    rebar.GetHostId();

                if (hostId == null ||
                    hostId == ElementId.InvalidElementId ||
                    !wallIds.Contains(hostId))
                {
                    continue;
                }

                RebarSnapshot snapshot =
                    CreateRebarSnapshot(
                        rebar);

                if (snapshot != null)
                {
                    rebars.Add(snapshot);
                }
            }

            const double toleranceMm = 2.0;

            double tolerance =
                ToFeet(toleranceMm);

            List<DuplicateRebarPair> duplicates =
                new List<DuplicateRebarPair>();

            for (int i = 0; i < rebars.Count; i++)
            {
                for (int j = i + 1; j < rebars.Count; j++)
                {
                    RebarSnapshot a = rebars[i];
                    RebarSnapshot b = rebars[j];

                    if (a.HostId != b.HostId)
                        continue;

                    if (Math.Abs(
                            a.DiameterFeet -
                            b.DiameterFeet) >
                        ToFeet(0.5))
                    {
                        continue;
                    }

                    if (!AreRebarCurvesCoincident(
                            a.Curves,
                            b.Curves,
                            tolerance))
                    {
                        continue;
                    }

                    duplicates.Add(
                        new DuplicateRebarPair
                        {
                            A = a,
                            B = b
                        });
                }
            }

            StringBuilder report =
                new StringBuilder();

            report.AppendLine(
                $"Проверено Rebar: {rebars.Count}");

            report.AppendLine(
                $"Допуск совпадения: {toleranceMm:F0} мм");

            if (duplicates.Count == 0)
            {
                report.Append(
                    "Возможных геометрических дублей не найдено.");

                return report.ToString();
            }

            report.AppendLine(
                $"Найдено возможных дублей: {duplicates.Count}");

            report.AppendLine();

            int shown = 0;
            const int maxShown = 30;

            foreach (DuplicateRebarPair pair in duplicates)
            {
                if (shown >= maxShown)
                    break;

                report.AppendLine(
                    $"Ø{ToMm(pair.A.DiameterFeet):F0} " +
                    $"| стена ID {pair.A.HostId.IntegerValue} " +
                    $"| Rebar {pair.A.Id.IntegerValue} / " +
                    $"{pair.B.Id.IntegerValue}");

                shown++;
            }

            if (duplicates.Count > maxShown)
            {
                report.AppendLine();
                report.Append(
                    $"Показаны первые {maxShown} из " +
                    $"{duplicates.Count} совпадений.");
            }

            return report.ToString();
        }


        // ============================================================
        // СНИМОК ГЕОМЕТРИИ REBAR
        // ============================================================

        private static RebarSnapshot CreateRebarSnapshot(
            Rebar rebar)
        {
            if (rebar == null)
                return null;

            IList<Curve> curves;

            try
            {
                curves =
                    rebar.GetCenterlineCurves(
                        false,
                        false,
                        false,
                        MultiplanarOption.IncludeOnlyPlanarCurves,
                        0);
            }
            catch
            {
                return null;
            }

            if (curves == null ||
                curves.Count == 0)
            {
                return null;
            }

            List<CurveSignature> signatures =
                new List<CurveSignature>();

            foreach (Curve curve in curves)
            {
                CurveSignature signature =
                    CreateCurveSignature(
                        curve);

                if (signature != null)
                {
                    signatures.Add(signature);
                }
            }

            if (signatures.Count == 0)
                return null;

            return new RebarSnapshot
            {
                Id = rebar.Id,
                HostId = rebar.GetHostId(),
                DiameterFeet =
                    GetRebarDiameterFeetForValidation(
                        rebar),
                Curves = signatures
            };
        }


        // ============================================================
        // ДИАМЕТР
        // ============================================================

        private static double GetRebarDiameterFeetForValidation(
            Rebar rebar)
        {
            if (rebar == null)
                return 0.0;

            RebarBarType barType =
                rebar.Document.GetElement(
                    rebar.GetTypeId())
                as RebarBarType;

            if (barType == null)
                return 0.0;

            Parameter parameter =
                barType.get_Parameter(
                    BuiltInParameter.REBAR_BAR_DIAMETER);

            if (parameter == null ||
                parameter.StorageType != StorageType.Double)
            {
                return 0.0;
            }

            return parameter.AsDouble();
        }


        // ============================================================
        // ПОДПИСЬ КРИВОЙ
        //
        // Для диагностики нам достаточно:
        // начало + середина + конец.
        // Это работает и для Line, и для Arc.
        // ============================================================

        private static CurveSignature CreateCurveSignature(
            Curve curve)
        {
            if (curve == null)
                return null;

            XYZ start;
            XYZ middle;
            XYZ end;

            try
            {
                start =
                    curve.GetEndPoint(0);

                end =
                    curve.GetEndPoint(1);

                middle =
                    curve.Evaluate(
                        0.5,
                        true);
            }
            catch
            {
                return null;
            }

            return new CurveSignature
            {
                Start = start,
                Middle = middle,
                End = end
            };
        }


        // ============================================================
        // СОВПАДАЮТ ЛИ ДВА REBAR
        // ============================================================

        private static bool AreRebarCurvesCoincident(
            List<CurveSignature> a,
            List<CurveSignature> b,
            double tolerance)
        {
            if (a == null ||
                b == null ||
                a.Count != b.Count)
            {
                return false;
            }

            bool direct =
                true;

            for (int i = 0; i < a.Count; i++)
            {
                if (!AreCurveSignaturesCoincident(
                        a[i],
                        b[i],
                        tolerance))
                {
                    direct = false;
                    break;
                }
            }

            if (direct)
                return true;

            // Revit может вернуть ту же геометрию
            // в обратном порядке кривых.
            for (int i = 0; i < a.Count; i++)
            {
                CurveSignature ca =
                    a[i];

                CurveSignature cb =
                    b[b.Count - 1 - i];

                if (!AreCurveSignaturesCoincidentReversed(
                        ca,
                        cb,
                        tolerance))
                {
                    return false;
                }
            }

            return true;
        }


        private static bool AreCurveSignaturesCoincident(
            CurveSignature a,
            CurveSignature b,
            double tolerance)
        {
            if (a == null || b == null)
                return false;

            bool sameDirection =
                IsPointNear(a.Start, b.Start, tolerance)
                &&
                IsPointNear(a.Middle, b.Middle, tolerance)
                &&
                IsPointNear(a.End, b.End, tolerance);

            bool oppositeDirection =
                IsPointNear(a.Start, b.End, tolerance)
                &&
                IsPointNear(a.Middle, b.Middle, tolerance)
                &&
                IsPointNear(a.End, b.Start, tolerance);

            return sameDirection ||
                   oppositeDirection;
        }


        private static bool AreCurveSignaturesCoincidentReversed(
            CurveSignature a,
            CurveSignature b,
            double tolerance)
        {
            return AreCurveSignaturesCoincident(
                a,
                b,
                tolerance);
        }


        private static bool IsPointNear(
            XYZ a,
            XYZ b,
            double tolerance)
        {
            if (a == null || b == null)
                return false;

            return a.DistanceTo(b) <= tolerance;
        }


        // ============================================================
        // ВНУТРЕННИЕ КЛАССЫ ДИАГНОСТИКИ
        // ============================================================

        private class RebarSnapshot
        {
            public ElementId Id;
            public ElementId HostId;
            public double DiameterFeet;
            public List<CurveSignature> Curves;
        }


        private class CurveSignature
        {
            public XYZ Start;
            public XYZ Middle;
            public XYZ End;
        }


        private class DuplicateRebarPair
        {
            public RebarSnapshot A;
            public RebarSnapshot B;
        }
    }
}
