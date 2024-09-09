// Copyright QUANTOWER LLC. © 2017-2023. All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics.Metrics;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Chart;
using System.Reflection.Metadata.Ecma335;
using System.Linq;

namespace BoomerangQT
{
    /// <summary>
    /// An example of strategy for working with one symbol. Add your code, compile it and run via Strategy Runner panel in the assigned trading terminal.
    /// Information about API you can find here: http://api.quantower.com
    /// </summary>
	public class BoomerangQT : Strategy
    {
        [InputParameter("Symbol", 10)]
        private Symbol symbol;

        [InputParameter("Account", 20)]
        public Account account;

        [InputParameter("Open of Range", 30)]
        public DateTime startTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 8, 30, 0, DateTimeKind.Local);

        [InputParameter("Close of Range", 40)]
        public DateTime endTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 15, 0, 0, DateTimeKind.Local);

        [InputParameter("Look for entry from", 50)]
        public DateTime detectionStartTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 17, 0, 0, DateTimeKind.Local);

        [InputParameter("Look for entry until", 60)]
        public DateTime detectionEndTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 17, 0, 0, DateTimeKind.Local);

        public override IList<SettingItem> Settings {
            get {
                var settings = base.Settings;

                // Customize datatime fields to show only times

                if (settings.GetItemByName("Open of Range") is SettingItemDateTime startTimeSi) {
                    startTimeSi.Format = DatePickerFormat.LongTime;
                }

                if (settings.GetItemByName("Close of Range") is SettingItemDateTime endTimeSi) {
                    endTimeSi.Format = DatePickerFormat.LongTime;
                }

                if (settings.GetItemByName("Detection Start Time") is SettingItemDateTime detectionStartTimeSi)
                {
                    detectionStartTimeSi.Format = DatePickerFormat.LongTime;
                }

                if (settings.GetItemByName("Detection End Time") is SettingItemDateTime detectionEndTimeSi)
                {
                    detectionEndTimeSi.Format = DatePickerFormat.LongTime;
                }

                return settings;
            }
            set { base.Settings = value; }
        }

        public override string[] MonitoringConnectionsIds => new string[] { this.symbol?.ConnectionId };

        private HistoricalData historicalData;

        private double rangeHigh = double.MinValue;
        private double rangeLow = double.MaxValue;
        private bool rangeActive = false;
        private bool detectionActive = false;
        private DateTime rangeStart;
        private DateTime rangeEnd;
        private DateTime detectionStart;
        private DateTime detectionEnd;
        private double stopLossPercentage = 0.003; // This could be open
        private double dcaPercentage1 = 0.0015; // This could be open
        private double dcaQuantity1 = 1; // This could be open
        private Position currentPosition;
        private int numberDCA = 0;
        private int numberDCAToBE = 1; // This could be open

        public BoomerangQT() : base()
        {
            // Defines strategy's name and description.
            this.Name = "BoomerangQT";
            this.Description = "Time Range with Box Drawing";
        }

        /// <summary>
        /// This function will be called after creating a strategy
        /// </summary>
        protected override void OnCreated()
        {
            // Add your code here
        }

        /// <summary>
        /// This function will be called after running a strategy
        /// </summary>
        protected override void OnRun()
        {
            if (this.symbol == null || this.account == null || this.symbol.ConnectionId != this.account.ConnectionId)
            {
                Log("Incorrect input parameters... Symbol or Account are not specified or they have diffent connectionID.", StrategyLoggingLevel.Error);
                return;
            }

            this.symbol = Core.GetSymbol(this.symbol?.CreateInfo());

            if (this.symbol != null)
            {
                this.symbol.NewQuote += this.SymbolOnNewQuote;
                this.symbol.NewLast += this.SymbolOnNewLast;
                historicalData = this.symbol.GetHistory(Period.MIN1, Core.TimeUtils.DateTimeUtcNow);
                historicalData.NewHistoryItem += OnBarClosed;

                // Add listener for position opening
                Core.PositionAdded += this.CoreOnPositionAdded;

            }

            // Add your code here
        }

        private void OnBarClosed(object sender, HistoryEventArgs e)
        {
            DateTime barTime = e.HistoryItem.TimeLeft;

            // Recalculate rangeStart and rangeEnd for each day based on the current bar's date (ignore the time)
            DateTime today = barTime.Date;

            // Set rangeStart and rangeEnd using only the time component of StartTime and EndTime for the current day
            rangeStart = today.AddHours(startTime.Hour).AddMinutes(startTime.Minute);
            rangeEnd = today.AddHours(endTime.Hour).AddMinutes(endTime.Minute);

            // Same for detectionStartTime and detectionEndTime
            detectionStart = today.AddHours(detectionStartTime.Hour).AddMinutes(detectionEndTime.Minute);
            detectionEnd = today.AddHours(detectionStartTime.Hour).AddMinutes(detectionEndTime.Minute);

            Log($"Range active: {rangeActive}");
            Log($"Checking bar at {barTime.ToString("HH:mm")}, Range Start: {rangeStart.ToString("HH:mm")}, Range End: {rangeEnd.ToString("HH:mm")}");

            // If the bar falls within the time range, track the high and low
            if (barTime >= rangeStart && barTime <= rangeEnd)
            {
                rangeActive = true;
                if (e.HistoryItem[PriceType.High] > rangeHigh)
                    rangeHigh = e.HistoryItem[PriceType.High];
                if (e.HistoryItem[PriceType.Low] < rangeLow)
                    rangeLow = e.HistoryItem[PriceType.Low];

                Log($"Tracking: High = {rangeHigh}, Low = {rangeLow}");

                this.numberDCA = 0;
            }
            else if (rangeActive && barTime > rangeEnd)
            {
                // Once the range ends, log the range details and reset for the next day
                Log($"Range box for {today.ToShortDateString()} drawn from {rangeStart.ToString("HH:mm")} to {rangeEnd.ToString("HH:mm")}, High: {rangeHigh}, Low: {rangeLow}");
                rangeActive = false;
            }

            if (barTime >= detectionStart && barTime <= detectionEnd)
            {
                detectionActive = true;

                // Detectar ruptura por encima o por debajo del rango usando el precio de cierre
                double closePrice = e.HistoryItem[PriceType.Close];

                if (closePrice > rangeHigh)
                {
                    PlaceTrade(Side.Sell);  // Short con TP en el rango bajo
                }
                else if (closePrice < rangeLow)
                {
                    PlaceTrade(Side.Buy);  // Long con TP en el rango alto
                }
            }
            else if (detectionActive && barTime > detectionEnd)
            {
                detectionActive = false;
                ResetRange();
            }
        }

        private void PlaceTrade(Side side)
        {
            var result = Core.Instance.PlaceOrder(new PlaceOrderRequestParameters
            {
                Symbol = this.symbol,
                Account = this.account,
                Side = side,
                OrderTypeId = OrderType.Market,
                Quantity = 1,
            });

            Log(result.Status == TradingOperationResultStatus.Failure ? "Error al colocar la orden." : "Orden colocada exitosamente.");
        }

        private void CheckDCA()
        {
            if (this.currentPosition == null)
            {
                return;
            }

            // Usar Ask para compras y Bid para ventas
            double currentPrice = this.currentPosition.Side == Side.Buy ? this.symbol.Ask : this.symbol.Bid;

            // Calcular el precio de activación del DCA
            double dcaTriggerPrice1 = this.currentPosition.Side == Side.Buy ? this.currentPosition.OpenPrice * (1 - dcaPercentage1) : this.currentPosition.OpenPrice * (1 + dcaPercentage1);

            // Si el precio alcanza el nivel para hacer DCA
            if ((this.currentPosition.Side == Side.Buy && currentPrice <= dcaTriggerPrice1) || (this.currentPosition.Side == Side.Sell && currentPrice >= dcaTriggerPrice1))
            {
                this.numberDCA++;

                // Abrir nueva operación DCA
                Log("DCA activado. Abriendo nueva operación...");

                var result = Core.Instance.PlaceOrder(new PlaceOrderRequestParameters
                {
                    Symbol = this.symbol,
                    Account = this.account,
                    Side = this.currentPosition.Side,
                    OrderTypeId = OrderType.Market,
                    Quantity = dcaQuantity1,
                });

                Log(result.Status == TradingOperationResultStatus.Failure ? "Error al colocar la orden DCA." : "Orden DCA colocada exitosamente.");

                // Update take profit after DCA
                if (this.numberDCA == this.numberDCAToBE)
                {
                    this.PlaceCloseOrder(this.currentPosition, CloseOrderType.TakeProfit);
                }
              
            }
        }

        private void CoreOnPositionAdded(Position position)
        {
            if (this.currentPosition != null)
                return;

            this.currentPosition = position;

            // Recalculate size of SL
            this.PlaceCloseOrder(this.currentPosition, CloseOrderType.StopLoss);
            this.PlaceCloseOrder(this.currentPosition, CloseOrderType.TakeProfit);
        }

        private void PlaceCloseOrder(Position position, CloseOrderType closeOrderType)
        {
            var request = new PlaceOrderRequestParameters
            {
                Symbol = this.symbol,
                Account = this.account,
                Side = position.Side == Side.Buy ? Side.Sell : Side.Buy,
                Quantity = position.Quantity, // This will update the quantity of the new orders to the current amount (initial and after DCA)
                PositionId = position.Id,
                AdditionalParameters = new List<SettingItem>
                {
                    new SettingItemBoolean(OrderType.REDUCE_ONLY, true)
                }
            };

            if (closeOrderType == CloseOrderType.StopLoss)
            {
                var orderType = this.symbol.GetAlowedOrderTypes(OrderTypeUsage.CloseOrder).FirstOrDefault(ot => ot.Behavior == OrderTypeBehavior.Stop);

                if (orderType == null)
                {
                    this.LogError("Can't find order type for SL");
                    return;
                }

                request.OrderTypeId = orderType.Id;

                // Calcular el SL como un porcentaje del precio de entrada para compras y ventas
                double stopLoss = position.OpenPrice * (1 + (position.Side == Side.Sell ? stopLossPercentage : -stopLossPercentage));

                if (position.StopLoss is Order)
                {
                    var cancelResult = Core.Instance.CancelOrder(position.StopLoss);
                    if (cancelResult.Status == TradingOperationResultStatus.Failure)
                    {
                        Log($"Failed to cancel order {position.StopLoss.Id}. Cannot proceed with update.");
                        return;
                    }
                    Log($"Order {position.StopLoss.Id} canceled successfully.");
                }

                request.TriggerPrice = stopLoss;
            }
            else
            {
                var orderType = this.symbol.GetAlowedOrderTypes(OrderTypeUsage.CloseOrder).FirstOrDefault(ot => ot.Behavior == OrderTypeBehavior.Limit);

                if (orderType == null)
                {
                    this.LogError("Can't find order type for TP");
                    return;
                }

                request.OrderTypeId = orderType.Id;

                if (position.TakeProfit is Order)
                {
                    var cancelResult = Core.Instance.CancelOrder(position.TakeProfit);
                    if (cancelResult.Status == TradingOperationResultStatus.Failure)
                    {
                        Log($"Failed to cancel order {position.TakeProfit.Id}. Cannot proceed with update.");
                        return;
                    }
                    Log($"Order {position.TakeProfit.Id} canceled successfully.");
                }

                if (this.numberDCA >= this.numberDCAToBE)
                {
                    request.Price = position.Side == Side.Buy ? position.OpenPrice + this.CalculateTicksForCommissions() : position.OpenPrice - this.CalculateTicksForCommissions();
                } 
                else
                {
                    request.Price = position.Side == Side.Buy ? rangeLow : rangeHigh;
                }
            }

            var result = Core.PlaceOrder(request);

            if (result.Status == TradingOperationResultStatus.Failure)
                this.LogError(result.Message);
        }

        private double CalculateTicksForCommissions()
        {
            double tickSize = this.symbol.TickSize;
            int ticksForCommissions = 4;  // Ajusta el número de ticks para cubrir comisiones
            return tickSize * ticksForCommissions;
        }

        private void ResetRange()
        {
            rangeHigh = double.MinValue;
            rangeLow = double.MaxValue;
            rangeActive = false;
        }

        /// <summary>
        /// This function will be called after stopping a strategy
        /// </summary>
        protected override void OnStop()
        {
            if (this.symbol != null)
            {
                this.symbol.NewQuote -= SymbolOnNewQuote;
                this.symbol.NewLast -= SymbolOnNewLast;
            }

            if (historicalData != null)
            {
                historicalData.NewHistoryItem -= OnBarClosed;
            }

            // Add your code here
        }

        /// <summary>
        /// This function will be called after removing a strategy
        /// </summary>
        protected override void OnRemove()
        {
            this.symbol = null;
            this.account = null;
            // Add your code here
        }

        /// <summary>
        /// Use this method to provide run time information about your strategy. You will see it in StrategyRunner panel in trading terminal
        /// </summary>
        protected override void OnInitializeMetrics(Meter meter)
        {
            base.OnInitializeMetrics(meter);

            meter.CreateObservableCounter("range-active", () => this.rangeActive, description: "Range Active");
            meter.CreateObservableCounter("detection-active", () => this.detectionActive, description: "Detection Active");
        }

        private void SymbolOnNewQuote(Symbol symbol, Quote quote)
        {
            // Add your code here
        }

        private void SymbolOnNewLast(Symbol symbol, Last last)
        {
            // Add your code here
            this.CheckDCA();
        }
    }
}
