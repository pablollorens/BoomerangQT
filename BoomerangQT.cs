// BoomerangQT Strategy with BE Activation and Configurable Timeframe (Corrected)

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
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

        [InputParameter("Open of Range", 30)]
        public DateTime startTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 12, 25, 0, DateTimeKind.Local);

        [InputParameter("Close of Range", 40)]
        public DateTime endTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 12, 30, 0, DateTimeKind.Local);

        [InputParameter("Look for entry from", 50)]
        public DateTime detectionStartTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 12, 30, 0, DateTimeKind.Local);

        [InputParameter("Look for entry until", 60)]
        public DateTime detectionEndTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 22, 0, 0, DateTimeKind.Local);

        [InputParameter("Close Positions At", 70)]
        public DateTime closePositionsAt = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 22, 0, 0, DateTimeKind.Local);

        [InputParameter("Stop Loss Percentage", 80)]
        public double stopLossPercentage = 0.35;

        [InputParameter("Enable Break Even", 85, variants: new object[] { "True", true, "False", false })]
        public bool enableBreakEven = true;

        [InputParameter("Number of DCA to Break Even", 90)]
        public int numberDCAToBE = 1;

        // DCA Level 1 Parameters
        [InputParameter("Enable DCA Level 1", 100, variants: new object[] { "True", true, "False", false })]
        public bool enableDcaLevel1 = true;

        [InputParameter("DCA Level 1 Trigger Percentage", 101)]
        public double dcaPercentage1 = 0.15;

        [InputParameter("DCA Level 1 Quantity", 102)]
        public double dcaQuantity1 = 1;

        // DCA Level 2 Parameters
        [InputParameter("Enable DCA Level 2", 110, variants: new object[] { "True", true, "False", false })]
        public bool enableDcaLevel2 = false;

        [InputParameter("DCA Level 2 Trigger Percentage", 111)]
        public double dcaPercentage2 = 0.004;

        [InputParameter("DCA Level 2 Quantity", 112)]
        public double dcaQuantity2 = 1;

        // DCA Level 3 Parameters
        [InputParameter("Enable DCA Level 3", 120, variants: new object[] { "True", true, "False", false })]
        public bool enableDcaLevel3 = false;

        [InputParameter("DCA Level 3 Trigger Percentage", 121)]
        public double dcaPercentage3 = 0.006;

        [InputParameter("DCA Level 3 Quantity", 122)]
        public double dcaQuantity3 = 1;

        public override string[] MonitoringConnectionsIds => new[] { symbol?.ConnectionId };

        // Override Settings to customize DateTime input parameters
        public override IList<SettingItem> Settings
        {
            get
            {
                var settings = base.Settings;

                // Customize DateTime fields to show only times
                if (settings.GetItemByName("Open of Range") is SettingItemDateTime startTimeSi)
                {
                    startTimeSi.Format = DatePickerFormat.LongTime;
                }

                if (settings.GetItemByName("Close of Range") is SettingItemDateTime endTimeSi)
                {
                    endTimeSi.Format = DatePickerFormat.LongTime;
                }

                if (settings.GetItemByName("Look for entry from") is SettingItemDateTime detectionStartTimeSi)
                {
                    detectionStartTimeSi.Format = DatePickerFormat.LongTime;
                }

                if (settings.GetItemByName("Look for entry until") is SettingItemDateTime detectionEndTimeSi)
                {
                    detectionEndTimeSi.Format = DatePickerFormat.LongTime;
                }

                if (settings.GetItemByName("Close Positions At") is SettingItemDateTime closePositionsAtSi)
                {
                    closePositionsAtSi.Format = DatePickerFormat.LongTime;
                }

                return settings;
            }
            set { base.Settings = value; }
        }

        // Private variables
        private HistoricalData historicalData;
        private double? rangeHigh = null;
        private double? rangeLow = null;
        private DateTime rangeStart;
        private DateTime rangeEnd;
        private DateTime detectionStart;
        private DateTime detectionEnd;
        private Position currentPosition;
        private int numberDCA;
        private Status strategyStatus = Status.WaitingForRange;

        // DCA levels list
        private List<DcaLevel> dcaLevels = new List<DcaLevel>();

        public BoomerangQT()
        {
            Name = "BoomerangQT";
            Description = "Range breakout strategy with multiple DCA levels and session end position closure";
        }

        protected override void OnRun()
        {
            Log("Strategy is starting.", StrategyLoggingLevel.Trading);

            if (!ValidateInputs()) return;

            if (this.symbol == null || this.account == null || this.symbol.ConnectionId != this.account.ConnectionId)
            {
                Log("Incorrect input parameters... Symbol or Account are not specified or they have diffent connectionID.", StrategyLoggingLevel.Error);
                return;
            }

            this.symbol = Core.GetSymbol(this.symbol?.CreateInfo());

            if (this.symbol == null)
            {
                Log("Failed to initialize symbol.", StrategyLoggingLevel.Error);
                Stop();
                return;
            }

            Log($"Symbol initialized: {this.symbol.Name}", StrategyLoggingLevel.Trading);

            // Map the timeframe string to the Period
            Period selectedPeriod;
            switch (timeframe)
            {
                case "MIN1":
                    selectedPeriod = Period.MIN1;
                    break;
                case "MIN2":
                    selectedPeriod = Period.MIN2;
                    break;
                case "MIN5":
                    selectedPeriod = Period.MIN5;
                    break;
                case "MIN15":
                    selectedPeriod = Period.MIN15;
                    break;
                default:
                    selectedPeriod = Period.MIN5;
                    Log($"Invalid timeframe selected. Defaulting to MIN5.", StrategyLoggingLevel.Error);
                    break;
            }

            historicalData = symbol.GetHistory(selectedPeriod, DateTime.UtcNow);
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

        private void InitializeDcaLevels()
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

        private bool ValidateInputs()
        {
            if (symbol == null)
            {
                Log("Symbol is not specified.", StrategyLoggingLevel.Error);
                Stop();
                return false;
            }

            if (account == null)
            {
                Log("Account is not specified.", StrategyLoggingLevel.Error);
                Stop();
                return false;
            }

            if (symbol.ConnectionId != account.ConnectionId)
            {
                Log("Symbol and Account have different connection IDs.", StrategyLoggingLevel.Error);
                Stop();
                return false;
            }

            // Validate that DCA trigger percentages are less than stop loss percentage
            foreach (var dcaLevel in new[] { new { Enabled = enableDcaLevel1, Percentage = dcaPercentage1, Level = 1 },
                                             new { Enabled = enableDcaLevel2, Percentage = dcaPercentage2, Level = 2 },
                                             new { Enabled = enableDcaLevel3, Percentage = dcaPercentage3, Level = 3 } })
            {
                if (dcaLevel.Enabled && dcaLevel.Percentage >= stopLossPercentage)
                {
                    Log($"DCA Level {dcaLevel.Level} trigger percentage ({dcaLevel.Percentage * 100}%) must be less than Stop Loss percentage ({stopLossPercentage * 100}%).", StrategyLoggingLevel.Error);
                    Stop();
                    return false;
                }
            }

            // Validate that closePositionsAt is after detectionEndTime
            if (closePositionsAt.TimeOfDay <= detectionEndTime.TimeOfDay)
            {
                Log("Close Positions At time must be after the Detection End Time.", StrategyLoggingLevel.Error);
                Stop();
                return false;
            }

            Log("Input parameters validated.", StrategyLoggingLevel.Trading);
            return true;
        }

        private void OnNewHistoryItem(object sender, HistoryEventArgs e)
        {
            if (!(e.HistoryItem is HistoryItemBar bar)) return;

            DateTime barTime = bar.TimeLeft.ToLocalTime();
            UpdateRangeTimes(barTime.Date);

            Log($"New bar at {barTime:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);

            switch (strategyStatus)
            {
                case Status.WaitingForRange:
                    UpdateRange(bar);
                    break;
                case Status.BreakoutDetection:
                    DetectBreakout();
                    break;
                case Status.ManagingTrade:
                    MonitorTrade();
                    break;
            }
        }

        private void UpdateRangeTimes(DateTime date)
        {
            rangeStart = new DateTime(date.Year, date.Month, date.Day, startTime.Hour, startTime.Minute, startTime.Second, DateTimeKind.Local);
            rangeEnd = new DateTime(date.Year, date.Month, date.Day, endTime.Hour, endTime.Minute, endTime.Second, DateTimeKind.Local);
            detectionStart = new DateTime(date.Year, date.Month, date.Day, detectionStartTime.Hour, detectionStartTime.Minute, detectionStartTime.Second, DateTimeKind.Local);
            detectionEnd = new DateTime(date.Year, date.Month, date.Day, detectionEndTime.Hour, detectionEndTime.Minute, detectionEndTime.Second, DateTimeKind.Local);

            if (detectionStart < rangeEnd)
                detectionStart = rangeEnd;

            // Update closePositionsAt to the same date
            closePositionsAt = new DateTime(date.Year, date.Month, date.Day, closePositionsAt.Hour, closePositionsAt.Minute, closePositionsAt.Second, DateTimeKind.Local);

            Log($"Range times updated. Range Start: {rangeStart:HH:mm}, Range End: {rangeEnd:HH:mm}, Detection Start: {detectionStart:HH:mm}, Detection End: {detectionEnd:HH:mm}, Close Positions At: {closePositionsAt:HH:mm}", StrategyLoggingLevel.Trading);
        }

        private void UpdateRange(HistoryItemBar bar)
        {
            DateTime barTime = bar.TimeLeft.ToLocalTime();
            if (barTime >= rangeStart && barTime <= rangeEnd)
            {
                rangeHigh = rangeHigh.HasValue ? Math.Max(rangeHigh.Value, bar.High) : bar.High;
                rangeLow = rangeLow.HasValue ? Math.Min(rangeLow.Value, bar.Low) : bar.Low;

                Log($"Range updated. High: {rangeHigh}, Low: {rangeLow}", StrategyLoggingLevel.Trading);

                numberDCA = 0;
            }
            else if (barTime > rangeEnd)
            {
                Log($"Range detection ended. Final Range - High: {rangeHigh}, Low: {rangeLow}", StrategyLoggingLevel.Trading);
                strategyStatus = Status.BreakoutDetection;
            }
        }

        private void DetectBreakout()
        {
            if (historicalData.Count < 2) return;

            var previousBar = historicalData[1] as HistoryItemBar;
            if (previousBar == null) return;

            DateTime previousBarTime = previousBar.TimeLeft.ToLocalTime();

            Log($"Checking for breakout at {previousBarTime:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Trading);

            if (previousBarTime >= detectionStart && previousBarTime <= detectionEnd)
            {
                if (rangeHigh.HasValue && previousBar.Close > rangeHigh.Value)
                {
                    Log($"Breakout above range high detected at {previousBar.Close}.", StrategyLoggingLevel.Trading);
                    PlaceTrade(Side.Sell);
                    strategyStatus = Status.ManagingTrade;
                }
                else if (rangeLow.HasValue && previousBar.Close < rangeLow.Value)
                {
                    Log($"Breakout below range low detected at {previousBar.Close}.", StrategyLoggingLevel.Trading);
                    PlaceTrade(Side.Buy);
                    strategyStatus = Status.ManagingTrade;
                }
                else
                {
                    Log("No breakout detected.", StrategyLoggingLevel.Trading);
                }
            }
            else if (previousBarTime > detectionEnd)
            {
                Log("Detection period ended without breakout.", StrategyLoggingLevel.Trading);
                ResetStrategy();
            }
        }

        private void MonitorTrade()
        {
            if (currentPosition == null) return;

            DateTime now = DateTime.Now.ToLocalTime();

            // Check if current time is beyond the position closure time
            if (now >= closePositionsAt)
            {
                Log("Closing position due to trading session end.", StrategyLoggingLevel.Trading);
                ClosePosition();
                ResetStrategy();
                return;
            }

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

        private void PlaceTrade(Side side)
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

        private void PlaceDCAOrder(double quantity)
        {
            Log($"Placing DCA order with quantity {quantity}.", StrategyLoggingLevel.Trading);

            var result = Core.Instance.PlaceOrder(new PlaceOrderRequestParameters
            {
                Symbol = symbol,
                Account = account,
                Side = currentPosition.Side,
                OrderTypeId = OrderType.Market,
                Quantity = quantity,
            });

            if (result.Status == TradingOperationResultStatus.Failure)
                Log($"Failed to place DCA order: {result.Message}", StrategyLoggingLevel.Error);
            else
            {
                Log($"DCA order of {quantity} placed successfully.", StrategyLoggingLevel.Trading);
                UpdateCloseOrders();
            }
        }

        private void ClosePosition()
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

        private void OnPositionAdded(Position position)
        {
            if (position.Symbol != symbol || position.Account != account) return;
            if (currentPosition != null) return;

            currentPosition = position;

            Log($"New position added. Side: {position.Side}, Quantity: {position.Quantity}, Open Price: {position.OpenPrice}", StrategyLoggingLevel.Trading);

            UpdateCloseOrders();
        }

        private void UpdateCloseOrders()
        {
            Log("Updating Stop Loss and Take Profit orders.", StrategyLoggingLevel.Trading);
            PlaceOrUpdateCloseOrder(CloseOrderType.StopLoss);
            PlaceOrUpdateCloseOrder(CloseOrderType.TakeProfit);
        }

        private void PlaceOrUpdateCloseOrder(CloseOrderType closeOrderType)
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

        private void CancelExistingOrder(Order existingOrder)
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

        private double CalculateStopLossPrice()
        {
            double stopLossPrice = currentPosition.Side == Side.Buy
                ? currentPosition.OpenPrice * (1 - stopLossPercentage)
                : currentPosition.OpenPrice * (1 + stopLossPercentage);

            Log($"Calculated Stop Loss Price: {stopLossPrice}", StrategyLoggingLevel.Trading);

            return stopLossPrice;
        }

        private double CalculateTakeProfitPrice()
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

        private double CalculateTicksForCommissions()
        {
            double ticksForCommissions = symbol.TickSize * 4; // Adjust ticks as needed
            Log($"Calculated ticks for commissions: {ticksForCommissions}", StrategyLoggingLevel.Trading);
            return ticksForCommissions;
        }

        private void ResetStrategy()
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

        protected override void OnStop()
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

        protected override void OnInitializeMetrics(Meter meter)
        {
            base.OnInitializeMetrics(meter);

            meter.CreateObservableGauge("RangeLow", GetRangeLow);
            meter.CreateObservableGauge("RangeHigh", GetRangeHigh);
            meter.CreateObservableGauge("CurrentPnL", GetCurrentPnL);
            meter.CreateObservableGauge("StrategyStatus", () => (double)strategyStatus);
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
            public double Quantity { get; set; }
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
