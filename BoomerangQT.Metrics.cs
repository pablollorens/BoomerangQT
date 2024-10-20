using System.Diagnostics.Metrics;
using System;
using TradingPlatform.BusinessLayer;

namespace BoomerangQT
{
    public partial class BoomerangQT
    {
        protected override void OnInitializeMetrics(Meter meter)
        {
            try
            {
                base.OnInitializeMetrics(meter);

                meter.CreateObservableGauge("RangeLow", GetRangeLow);
                meter.CreateObservableGauge("RangeHigh", GetRangeHigh);
                meter.CreateObservableGauge("CurrentPnL", GetCurrentPnL);
                meter.CreateObservableGauge("CurrentContractsUsed", GetCurrentContractsUsed);
                meter.CreateObservableGauge("NumberDCA", GetNumberDCA);
                meter.CreateObservableGauge("StrategyStatus", () => (double) strategyStatus);
            }
            catch (Exception ex)
            {
                Log($"Exception in OnInitializeMetrics: {ex.Message}", StrategyLoggingLevel.Error);
            }
        }

        private double GetRangeLow()
        {
            return rangeLow ?? 0.0;
        }

        private int GetNumberDCA()
        {
            return numberDCA;
        }

        private double GetRangeHigh()
        {
            return rangeHigh ?? 0.0;
        }

        private double GetCurrentContractsUsed()
        {
            return currentContractsUsed ?? 0.0;
        }

        private double GetCurrentPnL()
        {
            return currentPosition?.NetPnL.Value ?? 0.0;
        }
    }
}
