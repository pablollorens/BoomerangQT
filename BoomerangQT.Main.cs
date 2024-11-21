// BoomerangQT.Main.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Utils;

namespace BoomerangQT
{
    public partial class BoomerangQT : Strategy, ICurrentSymbol, ICurrentAccount
    {
        public Symbol CurrentSymbol { get; set; }

        public Account CurrentAccount { get; set; }
        public string timeframe = "MIN5";

        public DateTime startTime;
        public DateTime endTime;
        public DateTime detectionStartTime;
        public DateTime detectionEndTime;
        public DateTime closePositionsAtTime;

        public FirstEntryOption firstEntryOption = FirstEntryOption.MainEntry;

        public int initialQuantity = 2;
        public double stopLossPercentage = 0.4;

        public bool enableBreakEven = false;
        public int numberDCAToBE = 1;

        public TPType takeProfitType = TPType.OppositeSideOfRange;
        public double takeProfitPercentage = 0.5;
        public double takeProfitPoints = 10;

        // DCA Level Settings
        public bool enableDcaLevel1 = true;
        public double dcaPercentage1 = 0.02;
        public int dcaQuantity1 = 2;

        public bool enableDcaLevel2 = true;
        public double dcaPercentage2 = 0.06;
        public int dcaQuantity2 = 4;

        public bool enableDcaLevel3 = false;
        public double dcaPercentage3 = 0.10;
        public int dcaQuantity3 = 1;

        private double minimumRangeSize = 0;

        public override string[] MonitoringConnectionsIds => CurrentSymbol != null ? new[] { CurrentSymbol.ConnectionId } : new string[0];

        // Private variables
        private HistoricalData historicalData;
        private double? rangeHigh = null;
        private double? rangeLow = null;
        private DateTimeOffset rangeStart;
        private DateTimeOffset rangeEnd;
        private DateTimeOffset detectionStart;
        private DateTimeOffset detectionEnd;
        private DateTimeOffset closePositionsAt;
        private Position currentPosition = null;
        private TimeZoneInfo selectedTimeZone;
        private int executedDCALevel = 0; // It will make sense to have 1,2 or 3 (if DCAs are being used)
        private Status strategyStatus = Status.WaitingForRange;
        private int expectedContracts = 0; // amount of contracts we are supposed to be using
        private string stopLossOrderId;
        private string takeProfitOrderId;
        private Period selectedPeriod;
        private HistoryItemBar previousBar = null;
        private double? currentContractsUsed = 0;
        private double? stopLossGlobalPrice = null;
        private double? openPrice = null;
        private Side? strategySide = null;

        private double totalNetPl;
        private double totalGrossPl;
        private double totalFee;

        public BreakevenOption breakevenOption = BreakevenOption.EveryDcaLevel; // Set default as needed
        public TpAdjustmentType tpAdjustmentType = TpAdjustmentType.FixedPoints;
        public double tpAdjustmentValue = 0.0;

        private List<string> dcaOrders = new List<string>();

        // DCA Level Parameters
        private List<DcaLevel> dcaLevels = new List<DcaLevel>();

        public BoomerangQT() : base()
        {
            Name = "BoomerangQT";
            Description = "Range breakout strategy with multiple DCA levels and session end position closure";

            selectedTimeZone = Core.Instance.TimeUtils.SelectedTimeZone.TimeZoneInfo;

            // Inicializar startTime
            // Here is ok to use local time, because is the one in Quantower.
            startTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 6, 25, 0, DateTimeKind.Local);
            endTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 6, 30, 0, DateTimeKind.Local);
            detectionStartTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 6, 30, 0, DateTimeKind.Local);
            detectionEndTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 15, 30, 0, DateTimeKind.Local);
            closePositionsAtTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 16, 0, 0, DateTimeKind.Local);
        }

        protected override void OnRun()
        {
            // If you did any modification to the dates, they will be in your computer local time (not Quantower)
            // So we need to transform them using the Quantower timezone
            startTime = ConvertToLocalTime(startTime);
            endTime = ConvertToLocalTime(endTime);
            detectionStartTime = ConvertToLocalTime(detectionStartTime);
            detectionEndTime = ConvertToLocalTime(detectionEndTime);
            closePositionsAtTime = ConvertToLocalTime(closePositionsAtTime);

            DateTime today = DateTime.Today.ToLocalTime();

            UpdateRangeTimes(today);

            //Log($"Range Start (Eastern): {rangeStart}", StrategyLoggingLevel.Trading);

            this.totalNetPl = 0D;
            strategyStatus = Status.WaitingForRange;
            currentContractsUsed = 0;
            executedDCALevel = 0;
            currentPosition = null;
            rangeHigh = null;
            rangeLow = null;
            stopLossGlobalPrice = null;
            openPrice = null;
            strategySide = null;
            stopLossOrderId = null;
            takeProfitOrderId = null;
            expectedContracts = 0;

            try
            {
                Log("Strategy is starting.", StrategyLoggingLevel.Trading);

                if (!ValidateInputs())
                {
                    Log("Validation failed. Strategy stopped.", StrategyLoggingLevel.Error);
                    Stop();
                    return;
                }

                if (CurrentSymbol == null)
                {
                    Log("Symbol is not specified.", StrategyLoggingLevel.Error);
                    Stop();
                    return;
                }

                Log($"Symbol initialized: {CurrentSymbol.Name}", StrategyLoggingLevel.Trading);

                // Map the timeframe string to the Period enum
                selectedPeriod = timeframe.ToUpper() switch
                {
                    "MIN1" or "1 MINUTE" => Period.MIN1,
                    "MIN2" or "2 MINUTES" => Period.MIN2,
                    "MIN5" or "5 MINUTES" => Period.MIN5,
                    "MIN15" or "15 MINUTES" => Period.MIN15,
                    _ => Period.MIN5
                };

                historicalData = CurrentSymbol.GetHistory(selectedPeriod, DateTime.Now);

                if (historicalData == null)
                {
                    Log("Failed to get historical data.", StrategyLoggingLevel.Error);
                    Stop();
                    return;
                }

                Log($"Historical data loaded with timeframe: {selectedPeriod}", StrategyLoggingLevel.Trading);

                InitializeDcaLevels();

                historicalData.HistoryItemUpdated += OnHistoryItemUpdated;
                Core.PositionAdded += OnPositionAdded;
                Core.PositionRemoved += OnPositionRemoved;
                Core.TradeAdded += this.Core_TradeAdded;

                currentContractsUsed = 0;
            }
            catch (Exception ex)
            {
                Log($"Exception in OnRun: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
                
            }
        }

        // Método auxiliar para manejar la conversión de DateTime
        private DateTime ConvertToLocalTime(DateTime dateTime)
        {
            //Log($"ConvertToLocalTime - before:{dateTime:yyyy-MM-dd HH:mm}");
            //Log($"SelectedTZ - {selectedTimeZone}");
            if (dateTime.Kind == DateTimeKind.Utc)
            {
                dateTime = TimeZoneInfo.ConvertTime(dateTime, selectedTimeZone);
                dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Local);
            }
            else
            {
                dateTime = dateTime.ToLocalTime();
            }
            //Log($"ConvertToLocalTime - after:{dateTime:yyyy-MM-dd HH:mm}");

            return dateTime;
        }

        private void OnHistoryItemUpdated(object sender, HistoryEventArgs e)
        {
            //Log($"We enter in HistoryItemUpdated");

            try
            {
                //if (strategyStatus == Status.ManagingTrade) {
                //    Log("We are MANAGING a TRADE!! and still entering here...!!!");
                //}


                if (!(e.HistoryItem is HistoryItemBar currentBar)) return;
                //if (historicalData.Count <= 1) return;

                //Log($"DateKind: {currentBar.TimeLeft.Kind}");

                //Log($"OnHistoryItemUpdated - currentTime:{currentBar.TimeLeft:yyyy-MM-dd HH:mm}");

                if (previousBar == null) previousBar = currentBar;
                
                DateTimeOffset previousBarTime = TimeZoneInfo.ConvertTime(previousBar.TimeLeft, selectedTimeZone);
                DateTimeOffset currentBarTime = TimeZoneInfo.ConvertTime(currentBar.TimeLeft, selectedTimeZone);

                //Log($"OnHistoryItemUpdated - previousBarTime:{previousBarTime.DateTime:yyyy-MM-dd HH:mm}");
                //Log($"OnHistoryItemUpdated - currentBarTime:{currentBarTime.DateTime:yyyy-MM-dd HH:mm}");

                //Log($"OnHistoryItemUpdated - previousBarTime kind:{previousBarTime.DateTime.Kind}");
                //Log($"OnHistoryItemUpdated - currentBarTime kind:{currentBarTime.DateTime.Kind}");

                //DateTime previousBarTime = previousBar.TimeLeft;
                //DateTime currentBarTime = currentBar.TimeLeft;

                Boolean closedBar = false;

                //Log($"currentBar: {currentBar}", StrategyLoggingLevel.Trading);
                //Log($"previousBar: {previousBar}", StrategyLoggingLevel.Trading);

                // Check if both bars' hours and minutes are the same
                if (currentBarTime.Hour == previousBarTime.Hour && currentBarTime.Minute == previousBarTime.Minute)
                {
                    //Log("The current and previous bar have the same hour and minute.", StrategyLoggingLevel.Trading);
                    // We can allow MonitorTrade and Range formation with this bars but not BreakoutDetection
                }
                else
                {
                    //Log($"The current and previous bar have different hour and/or minute, which means previousBar is closed at {previousBarTime:HH:mm}", StrategyLoggingLevel.Trading);
                    closedBar = true;
                }

                UpdateRangeTimes(currentBarTime.Date); // Updates range to the current day

                //Log($"Date: {currentBarTime:yyyy-MM-dd HH:mm}", StrategyLoggingLevel.Trading);
                
                if (strategyStatus == Status.WaitingForRange)
                {
                    
                    //Log($"There are {Core.Positions.Count()} position in WaitingForRange", StrategyLoggingLevel.Trading);

                    // We need to check if for any reason there is an open position on the asset we're trading and close them
                    if (Core.Positions.Length > 0)
                    {
                        foreach (Position position in Core.Positions)
                        {
                            if (position.Symbol.Name.StartsWith(CurrentSymbol.Name) && position.Account == CurrentAccount)
                            {
                                Log($"We try to close this position: {position}");
                                Core.Instance.ClosePosition(position);
                            }
                        }
                    }

                    //Log($"There are {Core.Positions.Count()} positions open now", StrategyLoggingLevel.Trading);

                    // Update range on every tick
                    UpdateRange(currentBar);

                    //if (currentPosition != null)
                    //{
                    //    Log($"currentPosition: {currentPosition}", StrategyLoggingLevel.Trading);
                    //}
                }
                
                if (strategyStatus == Status.BreakoutDetection)
                {
                    
                    if (closedBar == true) {
                        DetectBreakout(previousBar);
                    }
                }
                else if (strategyStatus == Status.WaitingToEnter)
                {
                    
                }
                else if (strategyStatus == Status.ManagingTrade)
                {
                    // Monitor trade on every tick
                    // We're always checking until a trade is in play
                    //Log("We enter here 2");
                    MonitorTrade(currentBar);
                }

                previousBar = currentBar;
            }
            catch (Exception ex)
            {
                Log($"Exception in OnNewQuote: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void Core_TradeAdded(Trade obj)
        {
            if (obj.NetPnl != null)
                this.totalNetPl += obj.NetPnl.Value;

            if (obj.GrossPnl != null)
                this.totalGrossPl += obj.GrossPnl.Value;

            if (obj.Fee != null)
                this.totalFee += obj.Fee.Value;
        }

        /**
         * This function takes the date and retrieve
         */
        private void UpdateRangeTimes(DateTime date)
        {
            try
            {
                date = date.ToLocalTime();
                TimeSpan selectedUtcOffset = selectedTimeZone.GetUtcOffset(date);

                rangeStart = new DateTimeOffset(date.Year, date.Month, date.Day, startTime.Hour, startTime.Minute, 0, selectedUtcOffset);
                rangeEnd = new DateTimeOffset(date.Year, date.Month, date.Day, endTime.Hour, endTime.Minute, 0, selectedUtcOffset);
                detectionStart = new DateTimeOffset(date.Year, date.Month, date.Day, detectionStartTime.Hour, detectionStartTime.Minute, 0, selectedUtcOffset);
                detectionEnd = new DateTimeOffset(date.Year, date.Month, date.Day, detectionEndTime.Hour, detectionEndTime.Minute, 0, selectedUtcOffset);
                closePositionsAt = new DateTimeOffset(date.Year, date.Month, date.Day, closePositionsAtTime.Hour, closePositionsAtTime.Minute, 0, selectedUtcOffset);
                

                if (rangeEnd < rangeStart)
                {
                    rangeEnd = rangeEnd.AddDays(1);
                    detectionStart = detectionStart.AddDays(1);
                    detectionEnd = detectionEnd.AddDays(1);
                    closePositionsAt = closePositionsAt.AddDays(1);
                }

                if (detectionStart < rangeEnd)
                {
                    detectionStart = detectionStart.AddDays(1);
                    detectionEnd = detectionEnd.AddDays(1);
                    closePositionsAt = closePositionsAt.AddDays(1);
                }

                if (detectionEnd < detectionStart)
                {
                    detectionEnd = detectionEnd.AddDays(1);
                    closePositionsAt = closePositionsAt.AddDays(1);
                }

                if (closePositionsAt < detectionEnd)
                {
                    closePositionsAt = closePositionsAt.AddDays(1);
                }
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
                //DateTime currentTime = TimeZoneInfo.ConvertTime(bar.TimeLeft, selectedTimeZone);
                DateTime currentTime = bar.TimeLeft;

                //Log($"UpdateRange - original Time:{bar.TimeLeft:yyyy-MM-dd HH:mm:ss}");
                //Log($"UpdateRange - currentTime:{currentTime.DateTime:yyyy-MM-dd HH:mm:ss}");

                if (currentTime >= rangeStart && currentTime < rangeEnd)
                {
                    rangeHigh = rangeHigh.HasValue ? Math.Max(rangeHigh.Value, bar.High) : bar.High;
                    rangeLow = rangeLow.HasValue ? Math.Min(rangeLow.Value, bar.Low) : bar.Low;

                    //Log($"Range updated. High: {rangeHigh}, Low: {rangeLow}", StrategyLoggingLevel.Trading);

                    executedDCALevel = 0;
                }
                if (currentTime >= rangeEnd)
                {
                    if (rangeHigh.HasValue && rangeLow.HasValue)
                    {
                        Log($"Range for day {currentTime:yyyy-MM-dd} detection ended. Final Range - High: {rangeHigh}, Low: {rangeLow}", StrategyLoggingLevel.Trading);

                        double rangeSize = rangeHigh.Value - rangeLow.Value;
                        if (rangeSize < minimumRangeSize)
                        {
                            Log($"Range size {rangeSize} is less than the minimum required {minimumRangeSize}. Resetting strategy.", StrategyLoggingLevel.Trading);
                            ResetStrategy();
                            return;
                        }
                        else
                        {
                            strategyStatus = Status.BreakoutDetection;
                        }

                        strategyStatus = Status.BreakoutDetection;
                    }
                    else
                    {
                        //Log("No valid range detected during range period. Remaining in WaitingForRange.", StrategyLoggingLevel.Trading);
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
                DateTime currentTime = bar.TimeLeft;

                //Log($"DetectBreakout - currentTime:{currentTime.AddHours(timeZoneOffset):yyyy-MM-dd HH:mm}");
                //Log($"DetectBreakout - detectionStart:{detectionStart:yyyy-MM-dd HH:mm}");
                //Log($"DetectBreakout - detectionEnd:{detectionEnd:yyyy-MM-dd HH:mm}");

                if (currentTime >= detectionStart && currentTime <= detectionEnd)
                {
                    if (rangeHigh.HasValue && bar.Close > rangeHigh.Value)
                    {
                        Log($"Breakout above range high detected at {bar.Close}.", StrategyLoggingLevel.Trading);
                        //Log($"{bar}");
                        openPrice = bar.Close;
                        PlaceSelectedEntry(Side.Sell);
                    }
                    else if (rangeLow.HasValue && bar.Close < rangeLow.Value)
                    {
                        Log($"Breakout below range low detected at {bar.Close}.", StrategyLoggingLevel.Trading);
                        //Log($"{bar}");
                        openPrice = bar.Close;
                        PlaceSelectedEntry(Side.Buy);
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
            DateTime currentTime = bar.TimeLeft;

            try
            {
                if (currentPosition == null)
                {
                    Log("Position has been closed externally (TP or SL hit). Resetting strategy.", StrategyLoggingLevel.Trading);
                    ResetStrategy();
                    return;
                }

                // Check if current time is beyond the position closure time
                if (currentTime >= closePositionsAt)
                {
                    Log("Closing position due to trading SESSION END.", StrategyLoggingLevel.Trading);
                    ClosePosition();
                    ResetStrategy();
                    return;
                }
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
                executedDCALevel = 0;
                strategyStatus = Status.WaitingForRange;
                currentContractsUsed = 0;
                stopLossGlobalPrice = null;
                openPrice = null;
                strategySide = null;
                expectedContracts = 0;
                stopLossOrderId = null;
                takeProfitOrderId = null;
                expectedContracts = 0;

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
                if (CurrentSymbol == null)
                {
                    Log("Symbol is not specified.", StrategyLoggingLevel.Error);
                    return false;
                }

                if (CurrentAccount == null)
                {
                    Log("Account is not specified.", StrategyLoggingLevel.Error);
                    return false;
                }

                if (CurrentSymbol.ConnectionId != CurrentAccount.ConnectionId)
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
                
                Core.PositionAdded -= OnPositionAdded;
                Core.PositionRemoved -= OnPositionRemoved;
                Core.TradeAdded -= this.Core_TradeAdded;
                base.OnStop();
            }
            catch (Exception ex)
            {
                Log($"Exception in OnStop: {ex.Message}", StrategyLoggingLevel.Error);
            }
        }
    }
}
