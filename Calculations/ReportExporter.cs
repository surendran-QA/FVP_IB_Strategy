using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FVP_IB_Strategy.Calculations
{
    public static class ReportExporter
    {
        private static readonly object _lockObj = new object();
        private static Dictionary<string, List<string>> _reportBuffers = new Dictionary<string, List<string>>();

        public static void InitializeReport(string filePath)
        {
            lock (_lockObj)
            {
                try
                {
                    if (string.IsNullOrEmpty(filePath)) return;
                    _reportBuffers[filePath] = new List<string> { "Day,OrderPlacedTime,EntryFillTime,ExitTime,Symbol,Side,Qty,EntryPrice,ExitPrice,PnL,Status,Result,Shape,IB_High,IB_Low,POC,VAH,VAL,LVN,HVN1,HVN2" };
                    
                    // --- IMMEDIATE HEADER FLUSH TO DISK (Immune to OnStop not firing in Background mode!) ---
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    File.WriteAllLines(filePath, _reportBuffers[filePath]);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to initialize and flush buffer for {filePath}: {ex.Message}");
                }
            }
        }

        public static void AppendReportRow(
            string filePath, 
            string day, 
            string orderPlacedTime, 
            string entryFillTime, 
            string exitTime, 
            string symbol, 
            string side, 
            double qty, 
            string entryPrice, 
            string exitPrice, 
            string pnl, 
            string status, 
            string result, 
            string shape, 
            string ibHigh, 
            string ibLow, 
            string poc, 
            string vah, 
            string val, 
            string lvn, 
            string hvn1, 
            string hvn2)
        {
            lock (_lockObj)
            {
                try
                {
                    if (string.IsNullOrEmpty(filePath)) return;
                    string row = $"{day},{orderPlacedTime},{entryFillTime},{exitTime},{symbol},{side},{qty},{entryPrice},{exitPrice},{pnl},{status},{result},{shape},{ibHigh},{ibLow},{poc},{vah},{val},{lvn},{hvn1},{hvn2}";
                    
                    if (!_reportBuffers.ContainsKey(filePath))
                    {
                        _reportBuffers[filePath] = new List<string> { "Day,OrderPlacedTime,EntryFillTime,ExitTime,Symbol,Side,Qty,EntryPrice,ExitPrice,PnL,Status,Result,Shape,IB_High,IB_Low,POC,VAH,VAL,LVN,HVN1,HVN2" };
                    }
                    _reportBuffers[filePath].Add(row);

                    // --- IMMEDIATE ATOMIC FLUSH TO DISK (Immune to OnStop not firing in Background mode!) ---
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    File.WriteAllLines(filePath, _reportBuffers[filePath]);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to append and flush row for {filePath}: {ex.Message}");
                }
            }
        }

        public static void FlushReports(string tradeReportPath, string allSignalsPath)
        {
            lock (_lockObj)
            {
                try
                {
                    FlushSingleReport(tradeReportPath);
                    FlushSingleReport(allSignalsPath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to flush in-memory reports to disk: {ex.Message}");
                }
            }
        }

        // ---- DIAGNOSTIC REPORT ----

        public static void InitializeDiagReport(string filePath)
        {
            lock (_lockObj)
            {
                try
                {
                    if (string.IsNullOrEmpty(filePath)) return;
                    _reportBuffers[filePath] = new List<string>
                    {
                        "Date,DayOfWeek,BufferBars,IsPrecise,IB_High,IB_Low,POC,VAH,VAL,Shape,Signal,EntryPrice,StopLoss,TakeProfit"
                    };
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    File.WriteAllLines(filePath, _reportBuffers[filePath]);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to initialize diag report {filePath}: {ex.Message}");
                }
            }
        }

        public static void AppendDiagRow(
            string filePath, string date, string dayOfWeek, int bufferBars, bool isPrecise,
            double ibHigh, double ibLow, double poc, double vah, double val,
            string shape, string signal, double entryPrice, double stopLoss, double takeProfit)
        {
            lock (_lockObj)
            {
                try
                {
                    if (string.IsNullOrEmpty(filePath)) return;
                    string ep = double.IsNaN(entryPrice) ? "-" : entryPrice.ToString("F2");
                    string sl = double.IsNaN(stopLoss)   ? "-" : stopLoss.ToString("F2");
                    string tp = double.IsNaN(takeProfit) ? "-" : takeProfit.ToString("F2");
                    string row = $"{date},{dayOfWeek},{bufferBars},{isPrecise},{ibHigh:F2},{ibLow:F2},{poc:F2},{vah:F2},{val:F2},{shape},{signal},{ep},{sl},{tp}";

                    if (!_reportBuffers.ContainsKey(filePath))
                        _reportBuffers[filePath] = new List<string> { "Date,DayOfWeek,BufferBars,IsPrecise,IB_High,IB_Low,POC,VAH,VAL,Shape,Signal,EntryPrice,StopLoss,TakeProfit" };

                    _reportBuffers[filePath].Add(row);
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    File.WriteAllLines(filePath, _reportBuffers[filePath]);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to append diag row to {filePath}: {ex.Message}");
                }
            }
        }

        private static void FlushSingleReport(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            if (_reportBuffers.TryGetValue(filePath, out var rows) && rows.Count > 0)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    File.WriteAllLines(filePath, rows);
                    _reportBuffers.Remove(filePath); // Clear from memory after successful flush!
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to write file {filePath}: {ex.Message}");
                }
            }
        }


    }
}
