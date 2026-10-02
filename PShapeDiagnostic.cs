using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

using System;
using System.Linq;
using System.Text;

namespace RevitPlugin
{
    public partial class HelloRevit
    {
        private static void ShowPShapeDiagnostic(Document doc)
        {
            var shapes =
                new FilteredElementCollector(doc)
                .OfClass(typeof(RebarShape))
                .Cast<RebarShape>()
                .Where(x =>
                    x.Name.IndexOf(
                        "П-шка",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            StringBuilder result =
                new StringBuilder();

            result.AppendLine("П-ОБРАЗНЫЕ ФОРМЫ");
            result.AppendLine("======================");
            result.AppendLine();

            if (shapes.Count == 0)
            {
                result.AppendLine(
                    "Формы с названием «П-шка» не найдены.");

                TaskDialog.Show(
                    "Диагностика П-шек",
                    result.ToString());

                return;
            }

            foreach (RebarShape shape in shapes)
            {
                result.AppendLine(
                    "Форма: " + shape.Name);

                result.AppendLine(
                    "ID: " + shape.Id.IntegerValue);

                result.AppendLine(
                    "Параметры:");

                foreach (Parameter parameter in shape.Parameters)
                {
                    if (parameter == null ||
                        parameter.Definition == null)
                    {
                        continue;
                    }

                    string name =
                        parameter.Definition.Name;

                    string value = "";

                    try
                    {
                        value =
                            parameter.AsValueString();

                        if (string.IsNullOrWhiteSpace(value))
                        {
                            value =
                                parameter.AsString();
                        }

                        if (string.IsNullOrWhiteSpace(value))
                        {
                            if (parameter.StorageType ==
                                StorageType.Double)
                            {
                                value =
                                    parameter.AsDouble()
                                    .ToString("F4");
                            }
                            else if (parameter.StorageType ==
                                     StorageType.Integer)
                            {
                                value =
                                    parameter.AsInteger()
                                    .ToString();
                            }
                        }
                    }
                    catch
                    {
                        value = "?";
                    }

                    result.AppendLine(
                        "  " + name +
                        " = " + value);
                }

                result.AppendLine();
                result.AppendLine(
                    "----------------------");
                result.AppendLine();
            }

            TaskDialog.Show(
                "Диагностика П-шек",
                result.ToString());
        }
    }
}