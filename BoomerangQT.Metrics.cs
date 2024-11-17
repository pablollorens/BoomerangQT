using System.Diagnostics.Metrics;
using System;
using TradingPlatform.BusinessLayer;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;

namespace BoomerangQT
{
    public partial class BoomerangQT
    {
        protected override void OnInitializeMetrics(Meter meter)
        {
            try
            {
                base.OnInitializeMetrics(meter);
                meter.CreateObservableCounter("total-pl-gross", () => this.totalGrossPl, description: "Account global PnL");
            }
            catch (Exception ex)
            {
                Log($"Exception in OnInitializeMetrics: {ex.Message}", StrategyLoggingLevel.Error);
            }
        }

        [Obsolete]
        protected override List<StrategyMetric> OnGetMetrics()
        {
            List<StrategyMetric> result = base.OnGetMetrics();

            result.Add(new StrategyMetric() { Name = "Strategy Status", FormattedValue = this.getStatus() });
            AddBreakevenInformation(result);
            result.Add(new StrategyMetric() { Name = "Closing Positions At", FormattedValue = closePositionsAtTime.ToString("MM/dd/yyyy HH:mm") });
            result.Add(new StrategyMetric() { Name = "Detection End Time", FormattedValue = detectionEndTime.ToString("MM/dd/yyyy HH:mm") });
            result.Add(new StrategyMetric() { Name = "Detection Start Time", FormattedValue = detectionStartTime.ToString("MM/dd/yyyy HH:mm") });
            result.Add(new StrategyMetric() { Name = "End Time", FormattedValue = endTime.ToString("MM/dd/yyyy HH:mm") });
            result.Add(new StrategyMetric() { Name = "Start Time", FormattedValue = startTime.ToString("MM/dd/yyyy HH:mm") });
            result.Add(new StrategyMetric() { Name = "Timeframe", FormattedValue = timeframe });
            result.Add(new StrategyMetric() { Name = "Asset", FormattedValue = CurrentSymbol.Name });
            result.Add(new StrategyMetric() { Name = "Contracts used", FormattedValue = GetCurrentContractsUsed().ToString() });
            result.Add(new StrategyMetric() { Name = "In DCA #", FormattedValue = GetNumberDCA().ToString() });
            result.Add(new StrategyMetric() { Name = "Current PnL", FormattedValue = currentPosition?.GrossPnL.ToString() ?? "n/a" });
            result.Add(new StrategyMetric() { Name = "Range Low", FormattedValue = GetRangeLow().ToString() ?? "n/a" });
            result.Add(new StrategyMetric() { Name = "Range High", FormattedValue = GetRangeHigh().ToString() ?? "n/a" });
            result.Add(new StrategyMetric() { Name = "DCA sizes", FormattedValue = GetDCASizes() });
            result.Add(new StrategyMetric() { Name = "DCA %", FormattedValue = GetDCAPercentages() });
            

            return result;
        }

        private void AddBreakevenInformation(List<StrategyMetric> result)
        {
            if (enableBreakEven)
            {
                result.Add(new StrategyMetric() { Name = "TP Adjustment Value", FormattedValue = tpAdjustmentValue.ToString() });
                result.Add(new StrategyMetric() { Name = "TP Adjustment Type", FormattedValue = tpAdjustmentType.ToString() });
                result.Add(new StrategyMetric() { Name = "Breakeven Trigger", FormattedValue = breakevenOption.ToString() });
                result.Add(new StrategyMetric() { Name = "Breakeven", FormattedValue = "Enabled" });
            } else {
                result.Add(new StrategyMetric() { Name = "Breakeven", FormattedValue = "Disabled" });
            }
        }

        private string GetDCAPercentages()
        {
            var returned = "";
            int index = 0;
            int count = dcaLevels.Count;
            foreach (var dcaLevel in dcaLevels)
            {
                bool isLast = (index == count - 1);
                returned += dcaLevel.TriggerPercentage;
                if (!isLast)
                {
                    returned += ", ";
                }
                index++;
            }
            return returned;
        }

        private string GetDCASizes()
        {
            var returned = "";
            int index = 0;
            int count = dcaLevels.Count;
            foreach (var dcaLevel in dcaLevels)
            {
                bool isLast = (index == count - 1);
                returned += dcaLevel.Quantity;
                if (!isLast)
                {
                    returned += ", ";
                }
                index++;
            }
            return returned;
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

        private string getStatus()
        {
            string returnedValue = "---";

            switch(strategyStatus)
            {
                case Status.WaitingForRange:
                    returnedValue = "Waiting For Range"; 
                    break;
                case Status.BreakoutDetection:
                    returnedValue = "Breakout Detection"; 
                    break;
                case Status.WaitingToEnter: 
                    returnedValue = "Waiting To Enter"; 
                    break;
                case Status.ManagingTrade: 
                    returnedValue = "Managing Trade"; 
                    break;
            }

            return returnedValue;
        }
    }
}
