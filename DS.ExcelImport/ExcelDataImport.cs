﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DS.Interfaces;
using NPOI.SS.UserModel;
using DocumentFormat.OpenXml;
using System.Windows;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using OpenXML = DocumentFormat.OpenXml.Spreadsheet;

namespace DS.ExcelImport
{
    public class ExcelDataImport : IDataImport
    {
        private readonly ExcelSettings settings;
        private List<Interfaces.Row> rows;
        private int? colsNum = null;

        public ExcelDataImport(ExcelSettings settings)
        {
            this.settings = settings;
            rows = new List<Interfaces.Row>();
        }

        public IEnumerable<object> GetHeaders()
        {
            var firstRow = GetFirstRow();
            IEnumerable<object> headers = new List<object>();

            if (firstRow != null)
            {
                if (settings.UseFirstRowAsHeader)
                {
                    headers = firstRow;
                }
                else
                {
                    headers = GenerateHeaders(firstRow.Count());
                }
            }
            colsNum = headers.Skip(1).Count();
            return headers;
        }

        public IEnumerable<Interfaces.Row> Load(int skip, int take)
        {
            ValidateImportSettings();

            if (settings.UseFirstRowAsHeader)
            {
                skip += 1;
            }

            using (FileStream fileStream = new FileStream(settings.FileName, FileMode.Open, FileAccess.Read))
            {
                using (SpreadsheetDocument excel = SpreadsheetDocument.Open(fileStream, false))
                {
                    var workbook = excel.WorkbookPart;
                    var worksheet = workbook.WorksheetParts.First();
                    using (OpenXmlReader reader = OpenXmlReader.Create(worksheet))
                    {
                        try
                        {
                            FindExcelRow(reader);
                            NextRow(reader, take);
                            if (colsNum == null)
                            {
                                colsNum = GetExcelRowCells(workbook, reader).Count();
                            }
                            do
                            {
                                var cells = GetExcelRowCells(workbook, reader);
                                var row = GetRow(cells);
                                if (row != null && !rows.Select(arg => arg.Timestamp).Contains(row.Timestamp))
                                {
                                    rows.Add(row);
                                }
                            } while (NextRow(reader));
                        }
                        catch (Exception e)
                        {
                            MessageBox.Show(e.Message);
                        }
                    }
                }
            }
            return rows.AsEnumerable();
        }

        public IEnumerable<Interfaces.Row> LoadAll()
        {
            return Load(settings.SkipFirstRowsNum, Int32.MaxValue);
        }

        private IEnumerable<string> GetFirstRow()
        {
            ValidateImportSettings();
            IList<string> cells = new List<string>();
            
            using (FileStream fileStream = new FileStream(settings.FileName, FileMode.Open, FileAccess.Read))
            {
                using (SpreadsheetDocument excel = SpreadsheetDocument.Open(fileStream, false))
                {
                    var workbook = excel.WorkbookPart;
                    var worksheet = workbook.WorksheetParts.First();
                    using (OpenXmlReader reader = OpenXmlReader.Create(worksheet))
                    {
                        try
                        {
                            FindExcelRow(reader);
                            if (reader.ReadFirstChild())
                            {
                                cells = GetExcelRowCells(workbook, reader);
                            }
                        }
                        catch (Exception e)
                        {
                            MessageBox.Show(e.Message);
                        }
                    }
                }
            }
            return cells;
        }
        private void FindExcelRow(OpenXmlReader reader, int skip = 0)
        {
            while (reader.Read() && reader.ElementType != typeof(OpenXML.Row)) ;
            while (--skip > 0)
            {
                reader.ReadNextSibling();
            }
        }
        private bool NextRow(OpenXmlReader reader, int take = int.MaxValue)
        {
            while (reader.ReadNextSibling())
            {
                if (reader.ElementType == typeof(OpenXML.Row))
                {
                    if (take-- > 0)
                    {
                        reader.ReadFirstChild();
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            } 
            return false;
        }

        private IList<string> GetExcelRowCells(WorkbookPart workbook, OpenXmlReader reader)
        {
            IList<string> cells = new List<string>();
            do
            {
                if (reader.ElementType == typeof(OpenXML.Cell))
                {
                    cells.Add(GetCellValue(workbook, reader));
                }
            } while (reader.ReadNextSibling());
            return cells;
        }

        private string GetCellValue(WorkbookPart workbook, OpenXmlReader reader)
        {
            OpenXML.Cell c = (OpenXML.Cell)reader.LoadCurrentElement();
            if (c.DataType != null && c.DataType == OpenXML.CellValues.SharedString)
            {
                OpenXML.SharedStringItem ssi = workbook.SharedStringTablePart.
                SharedStringTable.Elements<OpenXML.SharedStringItem>().ElementAt
                (int.Parse(c.CellValue.InnerText));
                return ssi.Text.Text;
            }
            else
            {
                return c.CellValue.InnerText;
            }
        }

        private int GetColumnsNumber(ISheet worksheet)
        {
            int AllColsNum = 0;
            foreach (var item in worksheet)
            {
                int rowColsNum = (item as IRow).LastCellNum;
                AllColsNum = AllColsNum < rowColsNum ? rowColsNum : AllColsNum;
            }
            return AllColsNum;
        }

        private Interfaces.Row GetRow(IEnumerable<string> excelRow)
        {
            if (excelRow == null)
                return null;

            NumberFormatInfo numberFormat = new NumberFormatInfo
            {
                NumberDecimalSeparator = settings.NumberDelimiter
            };

            DateTime timestamp;
            Interfaces.Row row = null;
            double time;
            try
            {
                time = double.Parse(excelRow.First(), CultureInfo.InvariantCulture);
            }
            catch (Exception e)
            {

                throw e;
            }
            
            timestamp = DateTime.FromOADate(time);
            string ts = timestamp.ToString();
            if (DateTime.TryParseExact(ts, settings.DateTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp))
            {
                double sample;
                var samples = excelRow.Skip(1).Select(arg => double.TryParse(arg.ToString(), NumberStyles.Any, numberFormat, out sample) ? sample : double.NaN).Take(colsNum.Value);

                int samplesNum = samples.Count();
                if (samplesNum < colsNum)
                {
                    List<double> missingCells = new List<double>();
                    for (int i = samplesNum; i < colsNum; i++)
                    {
                        missingCells.Add(double.NaN);
                    }
                    samples = samples.Concat(missingCells);
                }

                row = new Interfaces.Row
                {
                    Timestamp = timestamp,
                    Samples = samples
                };
            }
            else
            {
                throw new Exception("Date time format is invalid");
            }
            return row;
        }

        private IEnumerable<object> GenerateHeaders(int number)
        {
            List<string> genericHeaders = new List<string>();
            int genericHeaderSize = number;
            genericHeaders.Add("timestamp");
            for (int i = 1; i < genericHeaderSize; i++)
            {
                genericHeaders.Add("C" + i);
            }
            return genericHeaders;
        }

        private void ValidateImportSettings()
        {
            if (!File.Exists(settings.FileName))
            {
                throw new FileNotFoundException();
            }
        }
    }
}
