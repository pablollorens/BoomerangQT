// BoomerangQT.Main.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace BoomerangQT
{
    public partial class BoomerangQT : Strategy
    {
        // Strategy parameters that are used in Main.cs
        private Symbol symbol;
        public Account account;
        public string timeframe = "MIN1";
        public int timeZoneOffset = -4; // Default to UTC±00:00

        public DateTime startTime = DateTime.Today.AddHours(6).AddMinutes(25);
        public DateTime endTime = DateTime.Today.AddHours(6).AddMinutes(30);
        public DateTime detectionStartTime = DateTime.Today.AddHours(6).AddMinutes(30);
        public DateTime detectionEndTime = DateTime.Today.AddHours(6).AddMinutes(45);
        public DateTime closePositionsAtTime = DateTime.Today.AddHours(16);

        public FirstEntryOption firstEntryOption = FirstEntryOption.MainEntry;
        public bool enterRegardlessOfRangeBreakout = false;

        public int initialQuantity = 1;
        public double stopLossPercentage = 0.35;

        public bool enableBreakEven = false;
        public int numberDCAToBE = 1;

        public TPType takeProfitType = TPType.OppositeSideOfRange;
        public double takeProfitPercentage = 0.5;
        public double takeProfitPoints = 10;

        public double breakevenPlusPoints = 0;

        public bool enableMarketReplayMode = false;

        // DCA Level Settings
        public bool enableDcaLevel1 = false;
        public double dcaPercentage1 = 0.15;
        public int dcaQuantity1 = 1;

        public bool enableDcaLevel2 = false;
        public double dcaPercentage2 = 0.35;
        public int dcaQuantity2 = 1;

        public bool enableDcaLevel3 = false;
        public double dcaPercentage3 = 0.35;
        public int dcaQuantity3 = 1;

        public override string[] MonitoringConnectionsIds => symbol != null ? new[] { symbol.ConnectionId } : new string[0];

        // Private variables
        private HistoricalData historicalData;
        private double? rangeHigh = null;
        private double? rangeLow = null;
        private DateTimeOffset rangeStart;
        private DateTimeOffset rangeEnd;
        private DateTimeOffset detectionStart;
        private DateTimeOffset detectionEnd;
        private DateTimeOffset closePositionsAt;
        private Position currentPosition;
        private TimeSpan selectedUtcOffset;
        private int numberDCA;
        private Status strategyStatus = Status.WaitingForRange;
        private string stopLossOrderId;
        private string takeProfitOrderId;
        private Period selectedPeriod;

        private List<IOrder> dcaOrders = new List<IOrder>();

        // DCA Level Parameters
        private List<DcaLevel> dcaLevels = new List<DcaLevel>();

        public BoomerangQT()
        {
            Name = "BoomerangQT";
            Description = "Range breakout strategy with multiple DCA levels and session end position closure";
        }

        protected override void OnRun()
        {
            try
            {
                selectedUtcOffset = TimeSpan.FromHours(timeZoneOffset);

                Log($"Selected UTC Offset: {selectedUtcOffset.TotalHours} hours");

                // Combine the date with the input times, treating input times as local times without time zone conversions
                UpdateRangeTimes(DateTime.Today);

                Log($"StartTime: {rangeStart:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);
                Log($"EndTime: {rangeEnd:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);
                Log($"DetectionStartTime: {detectionStart:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);
                Log($"DetectionEndTime: {detectionEnd:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);
                Log($"ClosePositionsAt: {closePositionsAt:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);

                if (!ValidateInputs())
                {
                    Log("Validation failed. Strategy stopped.", StrategyLoggingLevel.Error);
                    Stop();
                    return;
                }

                if (symbol == null)
                {
                    Log("Symbol is not specified.", StrategyLoggingLevel.Error);
                    Stop();
                    return;
                }

                Log($"Symbol initialized: {symbol.Name}", StrategyLoggingLevel.Trading);

                // Map the timeframe string to the Period enum
                selectedPeriod = timeframe.ToUpper() switch
                {
                    "MIN1" or "1 MINUTE" => Period.MIN1,
                    "MIN2" or "2 MINUTES" => Period.MIN2,
                    "MIN5" or "5 MINUTES" => Period.MIN5,
                    "MIN15" or "15 MINUTES" => Period.MIN15,
                    _ => Period.MIN5
                };

                historicalData = symbol.GetHistory(selectedPeriod, DateTime.Now);

                if (historicalData == null)
                {
                    Log("Failed to get historical data.", StrategyLoggingLevel.Error);
                    Stop();
                    return;
                }

                Log($"Historical data loaded with timeframe: {selectedPeriod}", StrategyLoggingLevel.Trading);

                InitializeDcaLevels();

                historicalData.HistoryItemUpdated += OnHistoryItemUpdated;
                //this.symbol.NewQuote += OnNewQuote; // TODO: I guess we are just working with the history item updated
                Core.PositionAdded += OnPositionAdded;
                Core.PositionRemoved += OnPositionRemoved;
                Core.Instance.LocalOrders.Updated += OnOrderUpdated;
            }
            catch (Exception ex)
            {
                Log($"Exception in OnRun: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void OnHistoryItemUpdated(object sender, HistoryEventArgs e)
        {
            Log($"item updated");

            try
            {
                if (!(e.HistoryItem is HistoryItemBar currentBar)) return;
                if (historicalData.Count <= 1) return;

                HistoryItemBar previousBar = historicalData[1] as HistoryItemBar;

                Boolean closedBar = false;

                // Check if both bars' hours and minutes are the same
                if (previousBar != null && currentBar.TimeLeft.Hour == previousBar.TimeLeft.Hour &&
                    currentBar.TimeLeft.Minute == previousBar.TimeLeft.Minute)
                {
                    Log("The current and previous bar have the same hour and minute.", StrategyLoggingLevel.Trading);
                }
                else
                {
                    Log($"The current and previous bar have different hour and/or minute, which means previousBar is closed at {previousBar:HH:mm}", StrategyLoggingLevel.Trading);
                    closedBar = true;
                }

                DateTime currentTime = currentBar.TimeLeft.AddHours(timeZoneOffset);

                UpdateRangeTimes(currentTime); // Updates range to the current day

                if (strategyStatus == Status.WaitingForRange)
                {
                    // Update range on every tick
                    UpdateRange(currentBar);

                    // If entering regardless of range breakout, enter immediately after range is formed
                    //if (enterRegardlessOfRangeBreakout && rangeHigh.HasValue && rangeLow.HasValue)
                    //{
                    //    PlaceSelectedEntry();
                    //    strategyStatus = Status.ManagingTrade;
                    //}
                }
                else if (strategyStatus == Status.BreakoutDetection)
                {
                    // Detect breakout only on closed bars
                    if (closedBar == true) {
                        DetectBreakout(previousBar);
                    }
                }
                else if (strategyStatus == Status.ManagingTrade)
                {
                    // Monitor trade on every tick
                    MonitorTrade(currentBar);
                }
            }
            catch (Exception ex)
            {
                Log($"Exception in OnNewQuote: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void UpdateRangeTimes(DateTime date)
        {
            try
            {
                TimeSpan selectedUtcOffset = TimeSpan.FromHours(timeZoneOffset);
                //Log($"Selected UTC Offset: {selectedUtcOffset.TotalHours} hours");

                rangeEnd = rangeEnd.AddMinutes(selectedPeriod.Duration.Minutes).AddSeconds(-1);

                // Combine the date with the input times, treating input times as local times without time zone conversions
                rangeStart = new DateTimeOffset(date.Year, date.Month, date.Day, startTime.Hour, startTime.Minute, 0, selectedUtcOffset);
                rangeEnd = new DateTimeOffset(date.Year, date.Month, date.Day, endTime.Hour, endTime.Minute, endTime.Second, selectedUtcOffset);
                detectionStart = new DateTimeOffset(date.Year, date.Month, date.Day, detectionStartTime.Hour, detectionStartTime.Minute, 0, selectedUtcOffset);
                detectionEnd = new DateTimeOffset(date.Year, date.Month, date.Day, detectionEndTime.Hour, detectionEndTime.Minute, 0, selectedUtcOffset);
                closePositionsAt = new DateTimeOffset(date.Year, date.Month, date.Day, closePositionsAtTime.Hour, closePositionsAtTime.Minute, 0, selectedUtcOffset);

                Log($"Timeoffset: {timeZoneOffset}");
                Log($"StartTime: {rangeStart:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);
                Log($"endTime: {rangeEnd:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);
                Log($"detectionStartTime: {detectionStart:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);
                Log($"detectionEndTime: {detectionEnd:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);
                Log($"ClosePositionsAt: {closePositionsAt:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);

                if (detectionStart < rangeEnd)
                    detectionStart = rangeEnd;

                if (rangeEnd < rangeStart)
                    rangeEnd = rangeEnd.AddDays(1);

                if (closePositionsAt <= detectionEnd)
                    closePositionsAt = closePositionsAt.AddDays(1);

                Log($"Range times updated. Range Start: {rangeStart:yyyy-MM-dd HH:mm}, Range End: {rangeEnd:yyyy-MM-dd HH:mm}, Detection Start: {detectionStart:yyyy-MM-dd HH:mm}, Detection End: {detectionEnd:yyyy-MM-dd HH:mm}, Close Positions At: {closePositionsAt:yyyy-MM-dd HH:mm}", StrategyLoggingLevel.Trading);
            }
            catch (Exception ex)
            {
                Log($"Exception in UpdateRangeTimes: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void UpdateRange(HistoryItemBar bar)
        {
            try
            {
                DateTime currentTime = bar.TimeLeft.AddHours(timeZoneOffset);

                if (currentTime >= rangeStart && currentTime < rangeEnd.AddMinutes(1))
                {
                    rangeHigh = rangeHigh.HasValue ? Math.Max(rangeHigh.Value, bar.High) : bar.High;
                    rangeLow = rangeLow.HasValue ? Math.Min(rangeLow.Value, bar.Low) : bar.Low;

                    Log($"Range updated. High: {rangeHigh}, Low: {rangeLow}", StrategyLoggingLevel.Trading);

                    numberDCA = 0;
                }
                else if (currentTime > rangeEnd)
                {
                    if (rangeHigh.HasValue && rangeLow.HasValue)
                    {
                        Log($"Range detection ended. Final Range - High: {rangeHigh}, Low: {rangeLow}", StrategyLoggingLevel.Trading);

                        strategyStatus = Status.BreakoutDetection;
                    }
                    else
                    {
                        Log("No valid range detected during range period. Remaining in WaitingForRange.", StrategyLoggingLevel.Trading);
                        rangeHigh = null;
                        rangeLow = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Exception in UpdateRange: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void DetectBreakout(HistoryItemBar bar)
        {
            try
            {
                DateTime currentTime = bar.TimeLeft.AddHours(timeZoneOffset);

                if (currentTime >= detectionStart && currentTime <= detectionEnd)
                {
                    if (rangeHigh.HasValue && bar.Close > rangeHigh.Value)
                    {
                        Log($"Breakout above range high detected at {bar.Close}.", StrategyLoggingLevel.Trading);
                        PlaceSelectedEntry(Side.Sell);
                        strategyStatus = Status.ManagingTrade;
                    }
                    else if (rangeLow.HasValue && bar.Close < rangeLow.Value)
                    {
                        Log($"Breakout below range low detected at {bar.Close}.", StrategyLoggingLevel.Trading);
                        PlaceSelectedEntry(Side.Buy);
                        strategyStatus = Status.ManagingTrade;
                    }
                }
                else if (currentTime > detectionEnd)
                {
                    Log("Detection period ended without breakout.", StrategyLoggingLevel.Trading);
                    ResetStrategy();
                }
            }
            catch (Exception ex)
            {
                Log($"Exception in DetectBreakout: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void MonitorTrade(HistoryItemBar bar)
        {
            try
            {
                if (currentPosition == null)
                {
                    Log("Position has been closed externally (TP or SL hit). Resetting strategy.", StrategyLoggingLevel.Trading);
                    ResetStrategy();
                    return;
                }

                DateTimeOffset currentTime = quote.Time;

                // Check if current time is beyond the position closure time
                if (currentTime >= closePositionsAt)
                {
                    Log("Closing position due to trading session end.", StrategyLoggingLevel.Trading);
                    ClosePosition();
                    ResetStrategy();
                    return;
                }

                // Check for DCA executions
                CheckDcaExecutions();
            }
            catch (Exception ex)
            {
                Log($"Exception in MonitorTrade: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void ResetStrategy()
        {
            try
            {
                Log("Resetting strategy for the next trading session.", StrategyLoggingLevel.Trading);

                rangeHigh = null;
                rangeLow = null;
                currentPosition = null;
                numberDCA = 0;
                strategyStatus = Status.WaitingForRange;

                // Cancel all DCA orders
                CancelDcaOrders();

                // Reset DCA levels
                foreach (var dcaLevel in dcaLevels)
                {
                    dcaLevel.Executed = false;
                    dcaLevel.OrderId = null;
                }

                Log("Strategy reset complete.", StrategyLoggingLevel.Trading);
            }
            catch (Exception ex)
            {
                Log($"Exception in ResetStrategy: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private bool ValidateInputs()
        {
            try { 
                if (symbol == null)
                {
                    Log("Symbol is not specified.", StrategyLoggingLevel.Error);
                    return false;
                }

                if (account == null)
                {
                    Log("Account is not specified.", StrategyLoggingLevel.Error);
                    return false;
                }

                if (symbol.ConnectionId != account.ConnectionId)
                {
                    Log("Symbol and Account have different connection IDs.", StrategyLoggingLevel.Error);
                    return false;
                }

                // Validate that DCA trigger percentages are less than stop loss percentage
                foreach (var dcaLevel in new[]
                {
                        new { Enabled = enableDcaLevel1, Percentage = dcaPercentage1, Level = 1 },
                        new { Enabled = enableDcaLevel2, Percentage = dcaPercentage2, Level = 2 },
                        new { Enabled = enableDcaLevel3, Percentage = dcaPercentage3, Level = 3 }
                    })
                {
                    if (dcaLevel.Enabled && dcaLevel.Percentage >= stopLossPercentage)
                    {
                        Log($"DCA Level {dcaLevel.Level} trigger percentage ({dcaLevel.Percentage * 100}%) must be less than Stop Loss percentage ({stopLossPercentage * 100}%).", StrategyLoggingLevel.Error);
                        return false;
                    }
                }

                Log("Input parameters validated.", StrategyLoggingLevel.Trading);
                return true;
            }
            catch (Exception ex)
            {
                Log($"Exception in ValidateInputs: {ex.Message}", StrategyLoggingLevel.Error);
                return false;
            }
        }

        protected override void OnStop()
        {
            try
            {
                Log("Strategy is stopping.", StrategyLoggingLevel.Trading);

                if (historicalData != null)
                {
                    historicalData.HistoryItemUpdated -= OnHistoryItemUpdated;
                    historicalData.Dispose();
                    historicalData = null;
                }

                historicalData.HistoryItemUpdated -= OnHistoryItemUpdated;
                Core.PositionAdded -= OnPositionAdded;
                Core.PositionRemoved -= OnPositionRemoved;
                Core.Instance.LocalOrders.Updated -= OnOrderUpdated;
                base.OnStop();
            }
            catch (Exception ex)
            {
                Log($"Exception in OnStop: {ex.Message}", StrategyLoggingLevel.Error);
            }
        }

        // The methods InitializeDcaLevels, PlaceSelectedEntry, ClosePosition, CancelDcaOrders, CheckDcaExecutions, etc.,
        // are defined in other partial class files (BoomerangQT.Dca.cs, BoomerangQT.Orders.cs, etc.)

        // Ensure that these methods are properly included in their respective files and accessible from this class.
    }
}
