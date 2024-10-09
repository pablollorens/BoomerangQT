﻿// BoomerangQT Strategy with BE Activation and Configurable Timeframe (Enhanced Error Handling)

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace BoomerangQT
{
    public enum Status
    {
        WaitingForRange,
        BreakoutDetection,
        ManagingTrade
    }

    public class BoomerangQT : Strategy
    {
        // Strategy parameters
        [InputParameter("Symbol", 10)]
        private Symbol symbol;

        [InputParameter("Account", 20)]
        public Account account;

        // Corrected Timeframe parameter using strings
        [InputParameter("Timeframe", 25, variants: new object[] { "1 Minute", "MIN1", "2 Minutes", "MIN2", "5 Minutes", "MIN5", "15 Minutes", "MIN15" })]
        public string timeframe = "MIN1";

        [InputParameter("Time Zone", 5, variants: new object[] {
            "UTC−12:00", -12,
            "UTC−11:00", -11,
            "UTC−10:00", -10,
            "UTC−09:00", -9,
            "UTC−08:00", -8,
            "UTC−07:00", -7,
            "UTC−06:00", -6,
            "UTC−05:00", -5,
            "UTC−04:00", -4,
            "UTC−03:00", -3,
            "UTC−02:00", -2,
            "UTC−01:00", -1,
            "UTC±00:00", 0,
            "UTC+01:00", 1,
            "UTC+02:00", 2,
            "UTC+03:00", 3,
            "UTC+04:00", 4,
            "UTC+05:00", 5,
            "UTC+06:00", 6,
            "UTC+07:00", 7,
            "UTC+08:00", 8,
            "UTC+09:00", 9,
            "UTC+10:00", 10,
            "UTC+11:00", 11,
            "UTC+12:00", 12,
            "UTC+13:00", 13,
            "UTC+14:00", 14
        })]
        public int timeZoneOffset = 0; // Default to UTC±00:00

        [InputParameter("Open of Range", 30)]
        public DateTime startTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 6, 25, 0, DateTimeKind.Local);

        [InputParameter("Close of Range", 40)]
        public DateTime endTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 6, 30, 0, DateTimeKind.Local);

        [InputParameter("Look for entry from", 50)]
        public DateTime detectionStartTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 6, 30, 0, DateTimeKind.Local);

        [InputParameter("Look for entry until", 60)]
        public DateTime detectionEndTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 6, 45, 0, DateTimeKind.Local);

        [InputParameter("Close Positions At", 70)]
        public DateTime closePositionsAtTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 7, 0, 0, DateTimeKind.Local);

        [InputParameter("Stop Loss Percentage", 80)]
        public double stopLossPercentage = 0.35;

        [InputParameter("Enable Break Even", 85)]
        public bool enableBreakEven = false;

        [InputParameter("Number of DCA to Break Even", 90)]
        public int numberDCAToBE = 1;

        // DCA Level 1 Parameters
        [InputParameter("Enable DCA Level 1", 100)]
        public bool enableDcaLevel1 = false;

        [InputParameter("DCA Level 1 Trigger Percentage", 101)]
        public double dcaPercentage1 = 0.15;

        [InputParameter("DCA Level 1 Quantity", 102)]
        public int dcaQuantity1 = 1;

        // DCA Level 2 Parameters
        [InputParameter("Enable DCA Level 2", 110)]
        public bool enableDcaLevel2 = false;

        [InputParameter("DCA Level 2 Trigger Percentage", 111)]
        public double dcaPercentage2 = 0.35;

        [InputParameter("DCA Level 2 Quantity", 112)]
        public int dcaQuantity2 = 1;

        // DCA Level 3 Parameters
        [InputParameter("Enable DCA Level 3", 120)]
        public bool enableDcaLevel3 = false;

        [InputParameter("DCA Level 3 Trigger Percentage", 121)]
        public double dcaPercentage3 = 0.35;

        [InputParameter("DCA Level 3 Quantity", 122)]
        public int dcaQuantity3 = 1;

        public override string[] MonitoringConnectionsIds => symbol != null ? new[] { symbol.ConnectionId } : new string[0];

        // Override Settings to customize DateTime input parameters
        

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

        // DCA levels list
        private List<DcaLevel> dcaLevels = new List<DcaLevel>();

        public override IList<SettingItem> Settings
        {
            get
            {
                var settings = base.Settings;

                // Customize DateTime fields to show only times
                foreach (var settingItem in settings)
                {
                    if (settingItem is SettingItemDateTime dateTimeSetting)
                    {
                        dateTimeSetting.Format = DatePickerFormat.Time;
                    }
                }

                return settings;
            }
            set { base.Settings = value; }
        }

        public BoomerangQT()
        {
            Name = "BoomerangQT";
            Description = "Range breakout strategy with multiple DCA levels and session end position closure";
        }

        protected override void OnRun()
        {
            Log($"Timeoffset: {timeZoneOffset}");
            Log($"StartTime: {startTime:HH:mm:ss}", StrategyLoggingLevel.Trading);
            Log($"endTime: {endTime:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);
            Log($"detectionStartTime: {detectionStartTime:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);
            Log($"detectionEndTime: {detectionEndTime:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);
            Log($"ClosePositionsAt: {closePositionsAtTime:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);


            selectedUtcOffset = TimeSpan.FromHours(timeZoneOffset);
            Log($"Selected UTC Offset: {selectedUtcOffset.TotalHours} hours");
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
                Period selectedPeriod;
                switch (timeframe.ToUpper())
                {
                    case "MIN1":
                    case "1 MINUTE":
                        selectedPeriod = Period.MIN1;
                        break;
                    case "MIN2":
                    case "2 MINUTES":
                        selectedPeriod = Period.MIN2;
                        break;
                    case "MIN5":
                    case "5 MINUTES":
                        selectedPeriod = Period.MIN5;
                        break;
                    case "MIN15":
                    case "15 MINUTES":
                        selectedPeriod = Period.MIN15;
                        break;
                    default:
                        selectedPeriod = Period.MIN5;
                        Log($"Invalid timeframe '{timeframe}' selected. Defaulting to MIN5.", StrategyLoggingLevel.Info);
                        break;
                }

                historicalData = symbol.GetHistory(selectedPeriod, DateTime.Now);

                if (historicalData == null)
                {
                    Log("Failed to get historical data.", StrategyLoggingLevel.Error);
                    Stop();
                    return;
                }

                Log($"Historical data loaded with timeframe: {selectedPeriod}", StrategyLoggingLevel.Trading);

                InitializeDcaLevels();

                historicalData.NewHistoryItem += OnNewHistoryItem;
                Core.PositionAdded += OnPositionAdded;
            }
            catch (Exception ex)
            {
                Log($"Exception in OnRun: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void InitializeDcaLevels()
        {
            try
            {
                dcaLevels.Clear();

                if (enableDcaLevel1)
                    dcaLevels.Add(new DcaLevel { TriggerPercentage = dcaPercentage1, Quantity = dcaQuantity1, LevelNumber = 1 });

                if (enableDcaLevel2)
                    dcaLevels.Add(new DcaLevel { TriggerPercentage = dcaPercentage2, Quantity = dcaQuantity2, LevelNumber = 2 });

                if (enableDcaLevel3)
                    dcaLevels.Add(new DcaLevel { TriggerPercentage = dcaPercentage3, Quantity = dcaQuantity3, LevelNumber = 3 });

                // Sort DCA levels by trigger percentage in ascending order
                dcaLevels = dcaLevels.OrderBy(d => d.TriggerPercentage).ToList();

                Log("DCA levels initialized.", StrategyLoggingLevel.Trading);

                foreach (var dca in dcaLevels)
                {
                    Log($"DCA Level {dca.LevelNumber}: Trigger at {dca.TriggerPercentage * 100}% with quantity {dca.Quantity}", StrategyLoggingLevel.Trading);
                }
            }
            catch (Exception ex)
            {
                Log($"Exception in InitializeDcaLevels: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private bool ValidateInputs()
        {
            try
            {
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

        private void OnNewHistoryItem(object sender, HistoryEventArgs e)
        {
            try
            {
                if (!(e.HistoryItem is HistoryItemBar currentBar)) return;
                if (historicalData.Count <= 1) return;

                HistoryItemBar bar = historicalData[1] as HistoryItemBar; // We take the previous candle which is properly closed
                DateTime barTime = bar.TimeLeft.AddHours(timeZoneOffset);

                DateTimeOffset newDateTimeOffset = new DateTimeOffset(
                   barTime.Year,
                   barTime.Month,
                   barTime.Day,
                   barTime.Hour,
                   barTime.Minute,
                   barTime.Second,
                   selectedUtcOffset
               );

                Log($"New bar properly closed at {barTime:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);
                Log($"strategyStatus: {strategyStatus}");

                UpdateRangeTimes(barTime.Date);

                switch (strategyStatus)
                {
                    case Status.WaitingForRange:
                        UpdateRange(bar);
                        break;
                    case Status.BreakoutDetection:
                        DetectBreakout(bar);
                        break;
                    case Status.ManagingTrade:
                        MonitorTrade(bar);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"Exception in OnNewHistoryItem: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void UpdateRangeTimes(DateTime date)
        {
            try
            {
                TimeSpan selectedUtcOffset = TimeSpan.FromHours(timeZoneOffset);
                //Log($"Selected UTC Offset: {selectedUtcOffset.TotalHours} hours");

                // Combine the date with the input times, treating input times as local times without time zone conversions
                rangeStart = new DateTimeOffset(date.Year, date.Month, date.Day, startTime.Hour, startTime.Minute, 0, selectedUtcOffset);
                rangeEnd = new DateTimeOffset(date.Year, date.Month, date.Day, endTime.Hour, endTime.Minute, 0, selectedUtcOffset);
                detectionStart = new DateTimeOffset(date.Year, date.Month, date.Day, detectionStartTime.Hour, detectionStartTime.Minute, 0, selectedUtcOffset);
                detectionEnd = new DateTimeOffset(date.Year, date.Month, date.Day, detectionEndTime.Hour, detectionEndTime.Minute, 0, selectedUtcOffset);
                closePositionsAt = new DateTimeOffset(date.Year, date.Month, date.Day, closePositionsAtTime.Hour, closePositionsAtTime.Minute, 0, selectedUtcOffset);

                if (detectionStart < rangeEnd)
                    detectionStart = rangeEnd;

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
                DateTime barTime = bar.TimeLeft;

                if (barTime >= rangeStart && barTime <= rangeEnd)
                {
                    rangeHigh = rangeHigh.HasValue ? Math.Max(rangeHigh.Value, bar[PriceType.High]) : bar[PriceType.High];
                    rangeLow = rangeLow.HasValue ? Math.Min(rangeLow.Value, bar[PriceType.Low]) : bar[PriceType.Low];

                    Log($"Range updated. High: {rangeHigh}, Low: {rangeLow}", StrategyLoggingLevel.Trading);

                    numberDCA = 0;
                }
                else if (barTime > rangeEnd)
                {
                    if (rangeHigh.HasValue && rangeLow.HasValue)
                    {
                        Log($"Range detection ended. Final Range - High: {rangeHigh}, Low: {rangeLow}", StrategyLoggingLevel.Trading);
                        strategyStatus = Status.BreakoutDetection;
                    }
                    else
                    {
                        Log("No valid range detected during range period. Remaining in WaitingForRange.", StrategyLoggingLevel.Trading);
                        // Optionally, you can reset the rangeHigh and rangeLow to null if needed
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
                DateTime barTime = bar.TimeLeft.ToUniversalTime();

                Log($"Checking for breakout at {barTime:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);
                Log($"Detection Start {detectionStart:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Info);
                Log($"Detection End at {detectionEnd:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Info);

                if (barTime >= detectionStart && barTime <= detectionEnd)
                {
                    if (rangeHigh.HasValue && bar.Close > rangeHigh.Value)
                    {
                        Log($"Breakout above range high detected at {bar.Close}.", StrategyLoggingLevel.Trading);
                        PlaceTrade(Side.Sell);
                        strategyStatus = Status.ManagingTrade;
                    }
                    else if (rangeLow.HasValue && bar.Close < rangeLow.Value)
                    {
                        Log($"Breakout below range low detected at {bar.Close}.", StrategyLoggingLevel.Trading);
                        PlaceTrade(Side.Buy);
                        strategyStatus = Status.ManagingTrade;
                    }
                    else
                    {
                        Log("No breakout detected.", StrategyLoggingLevel.Trading);
                    }
                }
                else if (barTime > detectionEnd)
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
                if (currentPosition == null) return; // Maybe we should clean up any open order, but maybe they are closed with the position as well... lets see

                DateTime barTime = bar.TimeLeft.ToUniversalTime();

                // Check if current time is beyond the position closure time
                Log($"barTime: {barTime:yyyy-MM-dd HH:mm:ss}");
                Log($"closePositionsAt: {closePositionsAt:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Info);
                

                if (barTime >= closePositionsAt)
                {
                    Log("Closing position due to trading session end.", StrategyLoggingLevel.Trading);
                    ClosePosition();
                    ResetStrategy();
                    return;
                }

                return;

                double currentPrice = currentPosition.CurrentPrice;

                foreach (var dcaLevel in dcaLevels)
                {
                    if (dcaLevel.Executed) continue;

                    double triggerPrice = currentPosition.Side == Side.Buy
                        ? currentPosition.OpenPrice * (1 - dcaLevel.TriggerPercentage)
                        : currentPosition.OpenPrice * (1 + dcaLevel.TriggerPercentage);

                    if ((currentPosition.Side == Side.Buy && currentPrice <= triggerPrice)
                        || (currentPosition.Side == Side.Sell && currentPrice >= triggerPrice))
                    {
                        dcaLevel.Executed = true;
                        numberDCA++;
                        Log($"DCA Level {dcaLevel.LevelNumber} triggered at price {currentPrice}.", StrategyLoggingLevel.Trading);
                        PlaceDCAOrder(dcaLevel.Quantity);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Exception in MonitorTrade: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void PlaceTrade(Side side)
        {
            try
            {
                Log($"Placing {side} trade.", StrategyLoggingLevel.Trading);

                var result = Core.Instance.PlaceOrder(new PlaceOrderRequestParameters
                {
                    Symbol = symbol,
                    Account = account,
                    Side = side,
                    OrderTypeId = OrderType.Market,
                    Quantity = 1,
                });

                if (result.Status == TradingOperationResultStatus.Failure)
                    Log($"Failed to place order: {result.Message}", StrategyLoggingLevel.Error);
                else
                    Log("Order placed successfully.", StrategyLoggingLevel.Trading);
            }
            catch (Exception ex)
            {
                Log($"Exception in PlaceTrade: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void PlaceDCAOrder(int quantity)
        {
            try
            {
                Log($"Placing DCA order with quantity {quantity}.", StrategyLoggingLevel.Trading);

                var result = Core.Instance.PlaceOrder(new PlaceOrderRequestParameters
                {
                    Symbol = symbol,
                    Account = account,
                    Side = currentPosition.Side,
                    OrderTypeId = OrderType.Market,
                    Quantity = (double) quantity,
                });

                if (result.Status == TradingOperationResultStatus.Failure)
                    Log($"Failed to place DCA order: {result.Message}", StrategyLoggingLevel.Error);
                else
                {
                    Log($"DCA order of {quantity} placed successfully.", StrategyLoggingLevel.Trading);
                    UpdateCloseOrders();
                }
            }
            catch (Exception ex)
            {
                Log($"Exception in PlaceDCAOrder: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void ClosePosition()
        {
            try
            {
                if (currentPosition == null) return;

                Log("Closing current position.", StrategyLoggingLevel.Trading);

                var result = Core.Instance.ClosePosition(currentPosition);
                if (result.Status == TradingOperationResultStatus.Failure)
                    Log($"Failed to close position: {result.Message}", StrategyLoggingLevel.Error);
                else
                    Log("Position closed successfully.", StrategyLoggingLevel.Trading);

                currentPosition = null;
            }
            catch (Exception ex)
            {
                Log($"Exception in ClosePosition: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void OnPositionAdded(Position position)
        {
            try
            {
                Log($"We enter on the event OnPositionAdded");

                if (position.Symbol != symbol || position.Account != account) return;
                if (currentPosition != null) return;

                currentPosition = position;

                Log($"New position added. Side: {position.Side}, Quantity: {position.Quantity}, Open Price: {position.OpenPrice}", StrategyLoggingLevel.Trading);

                UpdateCloseOrders();
            }
            catch (Exception ex)
            {
                Log($"Exception in OnPositionAdded: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void UpdateCloseOrders()
        {
            try
            {
                Log("Updating Stop Loss and Take Profit orders.", StrategyLoggingLevel.Trading);
                PlaceOrUpdateCloseOrder(CloseOrderType.StopLoss);
                PlaceOrUpdateCloseOrder(CloseOrderType.TakeProfit);
            }
            catch (Exception ex)
            {
                Log($"Exception in UpdateCloseOrders: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void PlaceOrUpdateCloseOrder(CloseOrderType closeOrderType)
        {
            try
            {
                if (currentPosition == null) return;

                var request = new PlaceOrderRequestParameters
                {
                    Symbol = symbol,
                    Account = account,
                    Side = currentPosition.Side.Invert(),
                    Quantity = currentPosition.Quantity,
                    PositionId = currentPosition.Id,
                    AdditionalParameters = new List<SettingItem>
                    {
                        new SettingItemBoolean(OrderType.REDUCE_ONLY, true)
                    }
                };

                if (closeOrderType == CloseOrderType.StopLoss)
                {
                    var orderType = symbol.GetOrderType(OrderTypeBehavior.Stop);
                    if (orderType == null)
                    {
                        Log("Stop order type not found.", StrategyLoggingLevel.Error);
                        return;
                    }

                    request.OrderTypeId = orderType.Id;
                    request.TriggerPrice = CalculateStopLossPrice();
                    CancelExistingOrder(currentPosition.StopLoss);
                    Log($"Placing Stop Loss at {request.TriggerPrice}", StrategyLoggingLevel.Trading);
                }
                else
                {
                    var orderType = symbol.GetOrderType(OrderTypeBehavior.Limit);
                    if (orderType == null)
                    {
                        Log("Limit order type not found.", StrategyLoggingLevel.Error);
                        return;
                    }

                    request.OrderTypeId = orderType.Id;
                    request.Price = CalculateTakeProfitPrice();
                    CancelExistingOrder(currentPosition.TakeProfit);
                    Log($"Placing Take Profit at {request.Price}", StrategyLoggingLevel.Trading);
                }

                var result = Core.Instance.PlaceOrder(request);
                if (result.Status == TradingOperationResultStatus.Failure)
                    Log($"Failed to place {closeOrderType} order: {result.Message}", StrategyLoggingLevel.Error);
                else
                    Log($"{closeOrderType} order placed successfully.", StrategyLoggingLevel.Trading);
            }
            catch (Exception ex)
            {
                Log($"Exception in PlaceOrUpdateCloseOrder: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void CancelExistingOrder(Order existingOrder)
        {
            try
            {
                if (existingOrder != null)
                {
                    Log($"Cancelling existing order ID: {existingOrder.Id}", StrategyLoggingLevel.Trading);

                    var cancelResult = Core.Instance.CancelOrder(existingOrder);
                    if (cancelResult.Status == TradingOperationResultStatus.Failure)
                        Log($"Failed to cancel existing order: {cancelResult.Message}", StrategyLoggingLevel.Error);
                    else
                        Log("Existing order cancelled successfully.", StrategyLoggingLevel.Trading);
                }
            }
            catch (Exception ex)
            {
                Log($"Exception in CancelExistingOrder: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private double CalculateStopLossPrice()
        {
            try
            {
                Log($"OpenPrice: {currentPosition.OpenPrice}");
                Log($"Side: {currentPosition.Side}");
                Log($"stopLossPercentage: {stopLossPercentage}");

                double stopLossPrice = currentPosition.Side == Side.Buy
                    ? currentPosition.OpenPrice - currentPosition.OpenPrice * (stopLossPercentage / 100)
                    : currentPosition.OpenPrice + currentPosition.OpenPrice * (stopLossPercentage / 100);

                Log($"Calculated Stop Loss Price: {stopLossPrice}", StrategyLoggingLevel.Trading);

                return stopLossPrice;
            }
            catch (Exception ex)
            {
                Log($"Exception in CalculateStopLossPrice: {ex.Message}", StrategyLoggingLevel.Error);
                throw;
            }
        }

        private double CalculateTakeProfitPrice()
        {
            try
            {
                double takeProfitPrice;

                if (enableBreakEven && (numberDCA >= numberDCAToBE || currentPosition.IsBreakevenPossible()))
                {
                    double adjustment = CalculateTicksForCommissions();
                    takeProfitPrice = currentPosition.Side == Side.Buy
                        ? currentPosition.OpenPrice + adjustment
                        : currentPosition.OpenPrice - adjustment;

                    Log($"Adjusted Take Profit to break-even price: {takeProfitPrice}", StrategyLoggingLevel.Trading);
                }
                else
                {
                    takeProfitPrice = currentPosition.Side == Side.Buy ? (rangeHigh ?? currentPosition.OpenPrice) : (rangeLow ?? currentPosition.OpenPrice);
                    Log($"Set Take Profit to target price: {takeProfitPrice}", StrategyLoggingLevel.Trading);
                }

                return takeProfitPrice;
            }
            catch (Exception ex)
            {
                Log($"Exception in CalculateTakeProfitPrice: {ex.Message}", StrategyLoggingLevel.Error);
                throw;
            }
        }

        private double CalculateTicksForCommissions()
        {
            try
            {
                double ticksForCommissions = symbol.TickSize * 4; // Adjust ticks as needed
                Log($"Calculated ticks for commissions: {ticksForCommissions}", StrategyLoggingLevel.Trading);
                return ticksForCommissions;
            }
            catch (Exception ex)
            {
                Log($"Exception in CalculateTicksForCommissions: {ex.Message}", StrategyLoggingLevel.Error);
                throw;
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

                // Reset DCA levels
                foreach (var dcaLevel in dcaLevels)
                {
                    dcaLevel.Executed = false;
                }

                Log("Strategy reset complete.", StrategyLoggingLevel.Trading);
            }
            catch (Exception ex)
            {
                Log($"Exception in ResetStrategy: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        protected override void OnStop()
        {
            try
            {
                Log("Strategy is stopping.", StrategyLoggingLevel.Trading);

                if (historicalData != null)
                {
                    historicalData.NewHistoryItem -= OnNewHistoryItem;
                    historicalData.Dispose();
                    historicalData = null;
                }

                Core.PositionAdded -= OnPositionAdded;
                base.OnStop();
            }
            catch (Exception ex)
            {
                Log($"Exception in OnStop: {ex.Message}", StrategyLoggingLevel.Error);
            }
        }

        protected override void OnInitializeMetrics(Meter meter)
        {
            try
            {
                base.OnInitializeMetrics(meter);

                meter.CreateObservableGauge("RangeLow", GetRangeLow);
                meter.CreateObservableGauge("RangeHigh", GetRangeHigh);
                meter.CreateObservableGauge("CurrentPnL", GetCurrentPnL);
               // meter.CreateObservableGauge("StrategyStatus", () => (double)strategyStatus);
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

        private double GetRangeHigh()
        {
            return rangeHigh ?? 0.0;
        }

        private double GetCurrentPnL()
        {
            return currentPosition?.NetPnL.Value ?? 0.0;
        }

        // DCA Level class
        private class DcaLevel
        {
            public int LevelNumber { get; set; }
            public double TriggerPercentage { get; set; }
            public int Quantity { get; set; }
            public bool Executed { get; set; } = false;
        }
    }

    public static class Extensions
    {
        public static Side Invert(this Side side)
        {
            return side == Side.Buy ? Side.Sell : Side.Buy;
        }

        public static OrderType GetOrderType(this Symbol symbol, OrderTypeBehavior behavior)
        {
            return symbol.GetAlowedOrderTypes(OrderTypeUsage.CloseOrder)
                .FirstOrDefault(ot => ot.Behavior == behavior);
        }
    }
}
