// BoomerangQT.Main.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Utils;

namespace BoomerangQT
{
    public partial class BoomerangQT : Strategy
    {
        // Strategy parameters that are used in Main.cs
        private Symbol symbol;
        public Account account;
        public string timeframe = "MIN1";

        //public DateTime startTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 6, 25, 0);
        //public DateTime endTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 6, 30, 0);
        //public DateTime detectionStartTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 6, 30, 0);
        //public DateTime detectionEndTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 15, 30, 0);
        //public DateTime closePositionsAtTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 16, 0, 0);

        public DateTime startTime = DateTime.Today.AddHours(6).AddMinutes(25);
        public DateTime endTime = DateTime.Today.AddHours(6).AddMinutes(30);
        public DateTime detectionStartTime = DateTime.Today.AddHours(6).AddMinutes(30);
        public DateTime detectionEndTime = DateTime.Today.AddHours(15).AddMinutes(30);
        public DateTime closePositionsAtTime = DateTime.Today.AddHours(16);

        public FirstEntryOption firstEntryOption = FirstEntryOption.MainEntry;

        public int initialQuantity = 1;
        public double stopLossPercentage = 0.35;

        public bool enableBreakEven = false;
        public int numberDCAToBE = 1;

        public TPType takeProfitType = TPType.OppositeSideOfRange;
        public double takeProfitPercentage = 0.5;
        public double takeProfitPoints = 10;

        public bool enableManualMode = false;

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
        //private TimeSpan selectedUtcOffset;
        private TimeZoneInfo easternZone;
        private int numberDCA;
        private Status strategyStatus = Status.WaitingForRange;
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

        public BreakevenOption breakevenOption = BreakevenOption.None; // Set default as needed
        public TpAdjustmentType tpAdjustmentType = TpAdjustmentType.FixedPoints;
        public double tpAdjustmentValue = 0.0;

        private List<string> dcaOrders = new List<string>();

        // DCA Level Parameters
        private List<DcaLevel> dcaLevels = new List<DcaLevel>();

        public BoomerangQT()
        {
            Name = "BoomerangQT";
            Description = "Range breakout strategy with multiple DCA levels and session end position closure";
        }

        protected override void OnRun()
        {
            easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

            DateTime today = DateTime.Now.Date;

            // Convert DateTime settings to DateTimeOffset with Eastern Time
            InitializeTimeVariables(easternZone);

            Log($"Range Start (Eastern): {rangeStart}", StrategyLoggingLevel.Trading);

            this.totalNetPl = 0D;

            //startTime = startTime.ToLocalTime();
            //endTime = endTime.ToLocalTime();
            //detectionStartTime = detectionStartTime.ToLocalTime();
            //detectionEndTime = detectionEndTime.ToLocalTime();
            //closePositionsAtTime = closePositionsAtTime.ToLocalTime();

            //Log($"StartTime: {startTime:yyyy-MM-dd HH:mm:ss}, kind: {startTime.Kind}", StrategyLoggingLevel.Trading);
            //Log($"endTime: {endTime:yyyy-MM-dd HH:mm:ss}, kind: {startTime.Kind}", StrategyLoggingLevel.Trading);
            //Log($"detectionStartTime: {detectionStartTime:yyyy-MM-dd HH:mm:ss}, kind: {startTime.Kind}", StrategyLoggingLevel.Trading);
            //Log($"detectionEndTime: {detectionEndTime:yyyy-MM-dd HH:mm:ss}, kind: {startTime.Kind}", StrategyLoggingLevel.Trading);
            //Log($"ClosePositionsAt: {closePositionsAtTime:yyyy-MM-dd HH:mm:ss}, kind: {startTime.Kind}", StrategyLoggingLevel.Trading);

            //startTime.AddHours(timeZoneOffset);
            //endTime.AddHours(timeZoneOffset);
            //detectionStartTime.AddHours(timeZoneOffset);
            //detectionEndTime.AddHours(timeZoneOffset);
            //closePositionsAtTime.AddHours(timeZoneOffset);

            // Combine the date with the input times, treating input times as local times without time zone conversions
            //rangeStart = new DateTime(startTime.Year, startTime.Month, startTime.Day, startTime.Hour, startTime.Minute, 0);
            //rangeEnd = new DateTime(endTime.Year, endTime.Month, endTime.Day, endTime.Hour, endTime.Minute, 0);
            //detectionStart = new DateTime(detectionStartTime.Year, detectionStartTime.Month, detectionStartTime.Day, detectionStartTime.Hour, detectionStartTime.Minute, 0);
            //detectionEnd = new DateTime(detectionEndTime.Year, detectionEndTime.Month, detectionEndTime.Day, detectionEndTime.Hour, detectionEndTime.Minute, 0);
            //closePositionsAt = new DateTime(closePositionsAtTime.Year, closePositionsAtTime.Month, closePositionsAtTime.Day, closePositionsAtTime.Hour, closePositionsAtTime.Minute, 0);

            //Log($"Timeoffset: {timeZoneOffset}");
            //Log($"StartTime: {rangeStart:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);
            //Log($"endTime: {rangeEnd:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);
            //Log($"detectionStartTime: {detectionStart:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);
            //Log($"detectionEndTime: {detectionEnd:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);
            //Log($"ClosePositionsAt: {closePositionsAt:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);

            try
            {
                Log("Strategy is starting.", StrategyLoggingLevel.Trading);

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
                Core.PositionAdded += OnPositionAdded;
                Core.PositionRemoved += OnPositionRemoved;
                Core.TradeAdded += this.Core_TradeAdded;
                //Core.Instance.LocalOrders.Updated += OnOrderUpdated;

                currentContractsUsed = 0;
            }
            catch (Exception ex)
            {
                Log($"Exception in OnRun: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void InitializeTimeVariables(TimeZoneInfo easternZone)
        {
            DateTime today = DateTime.Today;

            // Convert startTime to DateTimeOffset in Eastern Time
            rangeStart = new DateTimeOffset(today.Year, today.Month, today.Day,startTime.Hour, startTime.Minute, startTime.Second, easternZone.BaseUtcOffset);
            rangeStart = TimeZoneInfo.ConvertTime(rangeStart, easternZone);

            // Convert endTime to DateTimeOffset in Eastern Time
            rangeEnd = new DateTimeOffset(today.Year, today.Month, today.Day,endTime.Hour, endTime.Minute, endTime.Second, easternZone.BaseUtcOffset);
            rangeEnd = TimeZoneInfo.ConvertTime(rangeEnd, easternZone);

            // Convert detectionStartTime to DateTimeOffset in Eastern Time
            detectionStart = new DateTimeOffset(today.Year, today.Month, today.Day,detectionStartTime.Hour, detectionStartTime.Minute, detectionStartTime.Second, easternZone.BaseUtcOffset);
            detectionStart = TimeZoneInfo.ConvertTime(detectionStart, easternZone);

            // Convert detectionEndTime to DateTimeOffset in Eastern Time
            detectionEnd = new DateTimeOffset(today.Year, today.Month, today.Day,detectionEndTime.Hour, detectionEndTime.Minute, detectionEndTime.Second, easternZone.BaseUtcOffset);
            detectionEnd = TimeZoneInfo.ConvertTime(detectionEnd, easternZone);

            // Convert closePositionsAtTime to DateTimeOffset in Eastern Time
            closePositionsAt = new DateTimeOffset(today.Year, today.Month, today.Day,closePositionsAtTime.Hour, closePositionsAtTime.Minute, closePositionsAtTime.Second, easternZone.BaseUtcOffset);
            closePositionsAt = TimeZoneInfo.ConvertTime(closePositionsAt, easternZone);
        }


        private void OnHistoryItemUpdated(object sender, HistoryEventArgs e)
        {
            try
            {
                if (!(e.HistoryItem is HistoryItemBar currentBar)) return;
                //if (historicalData.Count <= 1) return;

                if (previousBar == null) previousBar = currentBar;

                DateTimeOffset previousBarTime = TimeZoneInfo.ConvertTime(previousBar.TimeLeft, easternZone);
                DateTimeOffset currentBarTime = TimeZoneInfo.ConvertTime(currentBar.TimeLeft, easternZone);

                //DateTime previousBarTime = previousBar.TimeLeft;
                //DateTime currentBarTime = currentBar.TimeLeft;

                Boolean closedBar = false;

                //Log($"currentBar: {currentBar}", StrategyLoggingLevel.Trading);
                //Log($"previousBar: {previousBar}", StrategyLoggingLevel.Trading);

                // Check if both bars' hours and minutes are the same
                if (currentBar.TimeLeft.Hour == previousBar.TimeLeft.Hour && currentBar.TimeLeft.Minute == previousBar.TimeLeft.Minute)
                {
                    //Log("The current and previous bar have the same hour and minute.", StrategyLoggingLevel.Trading);
                    // We can allow MonitorTrade and Range formation with this bars but not BreakoutDetection
                }
                else
                {
                    //Log($"The current and previous bar have different hour and/or minute, which means previousBar is closed at {previousBar:HH:mm}", StrategyLoggingLevel.Trading);
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
                            ClosePositionRequestParameters request = new ClosePositionRequestParameters();
                            if (position.Symbol.Name.StartsWith(symbol.Name))
                            {
                                Core.Instance.ClosePosition(position);
                            }
                        }
                    }

                    //Log($"There are {Core.Positions.Count()} positions open now", StrategyLoggingLevel.Trading);

                    // Update range on every tick
                    UpdateRange(currentBar, closedBar);

                    if (currentPosition != null)
                    {
                        Log($"currentPosition: {currentPosition}", StrategyLoggingLevel.Trading);
                    }
                    
                }
                
                if (strategyStatus == Status.BreakoutDetection && !enableManualMode)
                {
                    // Detect breakout only on closed bars and if manual mode is disabled
                    if (closedBar == true) {
                        DetectBreakout(previousBar);
                    }
                }
                else if (strategyStatus == Status.WaitingToEnter)
                {
                    if (currentPosition != null) strategyStatus = Status.ManagingTrade;
                }
                else if (strategyStatus == Status.ManagingTrade)
                {
                    // Monitor trade on every tick
                    // If manual mode is enabled and we enter a trade the status will become ManagingTrade, so basically we're always checking until a trade is in play
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

        private void UpdateRangeTimes(DateTime date)
        {
            try
            {
                //TimeSpan selectedUtcOffset = TimeSpan.FromHours(timeZoneOffset);
                //Log($"Selected UTC Offset: {selectedUtcOffset.TotalHours} hours");

                // Combine the date with the input times, treating input times as local times without time zone conversions
                rangeStart = new DateTime(date.Year, date.Month, date.Day, startTime.Hour, startTime.Minute, 0);
                rangeEnd = new DateTime(date.Year, date.Month, date.Day, endTime.Hour, endTime.Minute, 0);
                detectionStart = new DateTime(date.Year, date.Month, date.Day, detectionStartTime.Hour, detectionStartTime.Minute, 0);
                detectionEnd = new DateTime(date.Year, date.Month, date.Day, detectionEndTime.Hour, detectionEndTime.Minute, 0);
                closePositionsAt = new DateTime(date.Year, date.Month, date.Day, closePositionsAtTime.Hour, closePositionsAtTime.Minute, 0);

                //Log($"Timeoffset: {timeZoneOffset}");
                //Log($"StartTime: {rangeStart:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);
                //Log($"endTime: {rangeEnd:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);
                //Log($"detectionStartTime: {detectionStart:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);
                //Log($"detectionEndTime: {detectionEnd:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);
                //Log($"ClosePositionsAt: {closePositionsAt:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);

                if (detectionStart < rangeEnd)
                {
                    //Log($"Entramos aqui 1");
                    detectionStart = rangeEnd;
                }
                if (rangeEnd < rangeStart)
                {
                    //Log($"Entramos aqui 2");
                    rangeEnd = rangeEnd.AddDays(1);
                }

                if (closePositionsAt <= detectionEnd)
                {
                    //Log($"Entramos aqui 3");
                    closePositionsAt = closePositionsAt.AddDays(1);
                }

                //Log($"Range times updated. Range Start: {rangeStart:yyyy-MM-dd HH:mm}, Range End: {rangeEnd:yyyy-MM-dd HH:mm}");
                //Log($"Detection Start: {detectionStart:yyyy-MM-dd HH:mm}, Detection End: {detectionEnd:yyyy-MM-dd HH:mm}, Close Positions At: {closePositionsAt:yyyy-MM-dd HH:mm}", StrategyLoggingLevel.Trading);
            }
            catch (Exception ex)
            {
                Log($"Exception in UpdateRangeTimes: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void UpdateRange(HistoryItemBar bar, Boolean closedBar)
        {
            try
            {
                DateTimeOffset currentTime = TimeZoneInfo.ConvertTime(bar.TimeLeft, easternZone);

                //DateTime currentTime = bar.TimeLeft;

                //Log($"UpdateRange - currentTime:{currentTime.AddHours(timeZoneOffset):yyyy-MM-dd HH:mm}");
                //Log($"UpdateRange - rangeStart:{rangeStart:yyyy-MM-dd HH:mm}");
                //Log($"UpdateRange - rangeEnd:{rangeEnd:yyyy-MM-dd HH:mm}");

                if (currentTime >= rangeStart && currentTime <= rangeEnd)
                {
                    rangeHigh = rangeHigh.HasValue ? Math.Max(rangeHigh.Value, bar.High) : bar.High;
                    rangeLow = rangeLow.HasValue ? Math.Min(rangeLow.Value, bar.Low) : bar.Low;

                    //Log($"Range updated. High: {rangeHigh}, Low: {rangeLow}", StrategyLoggingLevel.Trading);

                    numberDCA = 0;
                }
                if (closedBar && currentTime > rangeEnd)
                {
                    if (rangeHigh.HasValue && rangeLow.HasValue)
                    {
                        Log($"Range for day {currentTime:yyyy-mm-dd} detection ended. Final Range - High: {rangeHigh}, Low: {rangeLow}", StrategyLoggingLevel.Trading);

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

            //Log($"MonitorTrade - currentTime:{currentTime.AddHours(timeZoneOffset):yyyy-MM-dd HH:mm}");

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
                currentContractsUsed = 0;
                stopLossGlobalPrice = null;
                openPrice = null;
                strategySide = null;

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
