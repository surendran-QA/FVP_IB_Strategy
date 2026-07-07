using System;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace CustomStrategies.Calculations
{
    public class InitialBalanceEngine
    {
        private ShapeDetector _shapeDetector = new ShapeDetector();
        private DataIngestionService _dataIngestion = new DataIngestionService();
        private VolumeProfileCalculator _profileCalc = new VolumeProfileCalculator();
        private SignalGenerator _signalGen = new SignalGenerator();

        /// <summary>
        /// PRIMARY OVERLOAD for Background Mode backtesting.
        /// Accepts pre-accumulated IB phase bars instead of scanning hdm backward.
        /// This avoids the hdm.Count=2 bug where GetHistory() does not pre-load history.
        /// This avoids the hdm.Count=2 bug where GetHistory() does not pre-load history.
        /// </summary>
        public bool CalculateIBFromBuffer(List<HistoryItemBar> ibPhaseBarBuffer, Symbol currentSymbol, DateTime targetDate, int profileStepTicks, int doubleDistMinTicks, out MarketData data, out bool isPrecise, double lvnThreshold = 0.12, double hvn2MinRatio = 0.30)
        {
            isPrecise = false;
            data = null;

            if (ibPhaseBarBuffer == null || ibPhaseBarBuffer.Count == 0)
                return false;

            // Derive ibHigh and ibLow from the accumulated buffer
            double ibHigh = ibPhaseBarBuffer.Max(b => b.High);
            double ibLow = ibPhaseBarBuffer.Min(b => b.Low);

            // 2. Volume Profile & Value Area Calculation
            if (!_profileCalc.CalculateProfile(ibPhaseBarBuffer, currentSymbol, profileStepTicks, ibLow, out data, out isPrecise, out var sortedProfile, out double maxVolume))
                return false;

            // Override High/Low from buffer scan (more accurate than profileCalc estimate)
            data.IB_High = ibHigh;
            data.IB_Low = ibLow;
            
            // Initialize session tracking bounds
            data.SessionHigh = ibHigh;
            data.SessionLow = ibLow;
            
            // Grab NY Open from chronological earliest bar (which is at the end of the buffer if populated in reverse, or front if chronological. Let's just find the oldest bar)
            var oldestBar = ibPhaseBarBuffer.OrderBy(b => b.TimeLeft).FirstOrDefault();
            if (oldestBar != null)
                data.NyOpenPrice = oldestBar.Open;

            // 3. Shape Classification
            _shapeDetector.ClassifyShape(data, sortedProfile, currentSymbol.TickSize, doubleDistMinTicks, maxVolume, lvnThreshold, hvn2MinRatio);

            // 4. Trade Signal Generation
            data.Signal = _signalGen.GenerateSignal(data, currentSymbol.TickSize, profileStepTicks);

            data.IsIBCalculated = true;
            data.LastCalculatedDate = targetDate.Date;

            return true;
        }

        /// <summary>
        /// LEGACY OVERLOAD — scans backward through hdm for IB bars.
        /// Only works correctly when hdm is fully pre-loaded (not in Background Mode).
        /// </summary>
        public bool CalculateIB(HistoricalData hdm, Symbol currentSymbol, DateTime targetDate, int ibDurationMinutes, int profileStepTicks, int doubleDistMinTicks, out MarketData data, out bool isPrecise, double lvnThreshold = 0.12, double hvn2MinRatio = 0.30)
        {
            isPrecise = false;
            data = null;

            // 1. Data Ingestion (SRP & DST Fix)
            var profileBars = _dataIngestion.GetProfileBars(hdm, targetDate, ibDurationMinutes, out double ibHigh, out double ibLow);
            if (profileBars.Count == 0)
                return false;

            if (!_profileCalc.CalculateProfile(profileBars, currentSymbol, profileStepTicks, ibLow, out data, out isPrecise, out var sortedProfile, out double maxVolume))
                return false;

            data.IB_High = ibHigh;
            data.IB_Low = ibLow;
            
            // Initialize session tracking bounds
            data.SessionHigh = ibHigh;
            data.SessionLow = ibLow;
            
            // Grab NY Open from chronological earliest bar
            var oldestBar = profileBars.OrderBy(b => b.TimeLeft).FirstOrDefault();
            if (oldestBar != null)
                data.NyOpenPrice = oldestBar.Open;

            // 3. Shape Classification
            _shapeDetector.ClassifyShape(data, sortedProfile, currentSymbol.TickSize, doubleDistMinTicks, maxVolume, lvnThreshold, hvn2MinRatio);

            // 4. Trade Signal Generation
            data.Signal = _signalGen.GenerateSignal(data, currentSymbol.TickSize, profileStepTicks);

            data.IsIBCalculated = true;
            data.LastCalculatedDate = targetDate.Date;

            return true;
        }
    }
}
