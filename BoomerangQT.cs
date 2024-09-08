// Copyright QUANTOWER LLC. © 2017-2023. All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics.Metrics;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Chart;

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
            }
            else if (detectionActive && barTime > detectionEnd)
            {
                detectionActive = false;
                ResetRange();
            }
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
        }
    }
}
