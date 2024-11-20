// BoomerangQT.Settings.cs
using System;
using System.Collections.Generic;
using System.Security.Principal;
using TradingPlatform.BusinessLayer;

namespace BoomerangQT
{
    public enum Status
    {
        WaitingForRange,
        BreakoutDetection,
        WaitingToEnter,
        ManagingTrade
    }

    public enum BreakevenOption
    {
        DcaLevel1 = 1,
        DcaLevel2 = 2,
        DcaLevel3 = 3,
        EveryDcaLevel = 4
    }

    public enum TpAdjustmentType
    {
        FixedPoints = 0,
        FixedPercentage = 1,
        RangeSize = 2
    }

    public enum FirstEntryOption
    {
        MainEntry = 0,
        DcaLevel1 = 1,
        DcaLevel2 = 2,
        DcaLevel3 = 3
    }

    public enum TPType
    {
        OppositeSideOfRange = 1,
        FixedPercentage = 2,
        FixedPoints = 3
    }

    public partial class BoomerangQT
    {
        public override IList<SettingItem> Settings
        {
            get
            {
                var settings = base.Settings;

                // Symbol
                settings.Add(new SettingItemSymbol("symbol", CurrentSymbol)
                {
                    Text = "Symbol",
                    SortIndex = 10
                });

                // Account
                settings.Add(new SettingItemAccount("account", CurrentAccount)
                {
                    Text = "Account",
                    SortIndex = 20
                });

                // Timeframe
                var timeframeOptions = new List<SelectItem>
                {
                    new SelectItem("1 Minute", "MIN1"),
                    new SelectItem("2 Minutes", "MIN2"),
                    new SelectItem("5 Minutes", "MIN5"),
                    new SelectItem("15 Minutes", "MIN15")
                };
                settings.Add(new SettingItemSelectorLocalized("timeframe", new SelectItem("", timeframe), timeframeOptions)
                {
                    Text = "Timeframe",
                    SortIndex = 25
                });

                // Time Zone
                //var timeZoneOptions = new List<SelectItem>();
                //for (int i = -12; i <= 14; i++)
                //{
                //    timeZoneOptions.Add(new SelectItem($"UTC{(i >= 0 ? "+" : "")}{i}:00", i));
                //}
                //settings.Add(new SettingItemSelectorLocalized("timeZoneOffset", timeZoneOffset, timeZoneOptions)
                //{
                //    Text = "Time Zone",
                //    SortIndex = 26
                //});

                // Time Settings
                settings.Add(new SettingItemDateTime("startTime", startTime)
                {
                    Text = "Open of Range",
                    SortIndex = 30
                });

                settings.Add(new SettingItemDateTime("endTime", endTime)
                {
                    Text = "Close of Range",
                    SortIndex = 40
                });

                settings.Add(new SettingItemDateTime("detectionStartTime", detectionStartTime)
                {
                    Text = "Look for entry from",
                    SortIndex = 50
                });

                settings.Add(new SettingItemDateTime("detectionEndTime", detectionEndTime)
                {
                    Text = "Look for entry until",
                    SortIndex = 60
                });

                settings.Add(new SettingItemDateTime("closePositionsAtTime", closePositionsAtTime)
                {
                    Text = "Close Positions At",
                    SortIndex = 70
                });

                settings.Add(new SettingItemInteger("minimumRangeSize", minimumRangeSize)
                {
                    Text = "Minimum Range Size",
                    SortIndex = 75,
                    Minimum = 1,
                    Maximum = 500,
                    Increment = 1,
                });

                // First Entry Option
                var firstEntryOptions = new List<SelectItem>
                {
                    new SelectItem("Main Entry", FirstEntryOption.MainEntry),
                    new SelectItem("DCA Level 1", FirstEntryOption.DcaLevel1),
                    new SelectItem("DCA Level 2", FirstEntryOption.DcaLevel2),
                    new SelectItem("DCA Level 3", FirstEntryOption.DcaLevel3)
                };
                settings.Add(new SettingItemSelectorLocalized("firstEntryOption", firstEntryOption, firstEntryOptions)
                {
                    Text = "First Entry",
                    SortIndex = 75
                });

                // Main Entry Quantity
                settings.Add(new SettingItemInteger("initialQuantity", initialQuantity)
                {
                    Text = "Main entry quantity",
                    SortIndex = 80,
                    Minimum=1,
                    Maximum=50,
                    Increment=1,
                    Relation = new SettingItemRelationVisibility("firstEntryOption", FirstEntryOption.MainEntry)
                });

                // Stop Loss Percentage
                settings.Add(new SettingItemDouble("stopLossPercentage", stopLossPercentage)
                {
                    Text = "Stop Loss Percentage",
                    SortIndex = 85,
                    Minimum = 0.01,
                    Maximum = 5,
                    Increment = 0.01,
                    DecimalPlaces = 2
                });

                // Take Profit Type
                var tpTypeOptions = new List<SelectItem>
                {
                    new SelectItem("Opposite Side of Range", TPType.OppositeSideOfRange),
                    new SelectItem("Fixed Percentage", TPType.FixedPercentage),
                    new SelectItem("Fixed Points", TPType.FixedPoints)
                };
                settings.Add(new SettingItemSelectorLocalized("takeProfitType", takeProfitType, tpTypeOptions)
                {
                    Text = "Take Profit Type",
                    SortIndex = 90
                });

                // Take Profit Percentage
                settings.Add(new SettingItemDouble("takeProfitPercentage", takeProfitPercentage)
                {
                    Text = "Take Profit Percentage",
                    SortIndex = 91,
                    Minimum = 0.01,
                    Maximum = 5,
                    Increment = 0.01,
                    DecimalPlaces = 2,
                    Relation = new SettingItemRelationVisibility("takeProfitType", TPType.FixedPercentage)
                });

                // Take Profit Points
                settings.Add(new SettingItemDouble("takeProfitPoints", takeProfitPoints)
                {
                    Text = "Take Profit Points",
                    SortIndex = 92,
                    Relation = new SettingItemRelationVisibility("takeProfitType", TPType.FixedPoints)
                });

                // DCA Levels
                settings.AddRange(GetDcaSettings());

                SettingItem enableBreakEvenSettingItem = new SettingItemBoolean("enableBreakEven", enableBreakEven)
                {
                    Text = "Enable Break Even",
                    SortIndex = 200
                };

                // Enable Break Even setting
                settings.Add(enableBreakEvenSettingItem);

                // Breakeven Options
                var breakevenOptions = new List<SelectItem>
                {
                    new SelectItem("DCA Level 1", BreakevenOption.DcaLevel1),
                    new SelectItem("DCA Level 2", BreakevenOption.DcaLevel2),
                    new SelectItem("DCA Level 3", BreakevenOption.DcaLevel3),
                    new SelectItem("Every DCA Level", BreakevenOption.EveryDcaLevel) // New option
                };

                settings.Add(new SettingItemSelectorLocalized("breakevenOption", breakevenOption, breakevenOptions)
                {
                    Text = "Breakeven Trigger",
                    SortIndex = 201,
                    Relation = new SettingItemRelationEnability("enableBreakEven", true)
                });


                // TP Adjustment Type
                var tpAdjustmentOptions = new List<SelectItem>
                {
                    new SelectItem("Fixed Points", TpAdjustmentType.FixedPoints),
                    new SelectItem("Fixed Percentage", TpAdjustmentType.FixedPercentage),
                    new SelectItem("Range Size", TpAdjustmentType.RangeSize)
                };

                SettingItem tpAdjustmentTypeSettingItem = new SettingItemSelectorLocalized("tpAdjustmentType", tpAdjustmentType, tpAdjustmentOptions)
                {
                    Text = "TP Adjustment Type",
                    SortIndex = 203,
                    Relation = new SettingItemRelationEnability("enableBreakEven", true)
                };

                settings.Add(tpAdjustmentTypeSettingItem);

                // TP Adjustment Value
                var tpAdjustmentValueSetting = new SettingItemDouble("tpAdjustmentValue", tpAdjustmentValue)
                {
                    Text = "TP Adjustment Value",
                    SortIndex = 204,
                    Minimum = 0.01,
                    Maximum = 100,
                    Increment = 0.01,
                    DecimalPlaces = 2,
                    Relation = new SettingItemMultipleRelation(
                        new SettingItemRelationEnability("enableBreakEven", true),
                        new SettingItemRelationVisibility("tpAdjustmentType", [TpAdjustmentType.FixedPoints, TpAdjustmentType.FixedPercentage])
                    )
                };

                settings.Add(tpAdjustmentValueSetting);

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
            set
            {
                base.Settings = value;

                if (value.TryGetValue("symbol", out Symbol symbolValue))
                    CurrentSymbol = symbolValue;

                if (value.TryGetValue("account", out Account accountValue))
                    CurrentAccount = accountValue;

                if (value.TryGetValue("timeframe", out string timeframeValue))
                    timeframe = timeframeValue;

                //if (value.TryGetValue("timeZoneOffset", out int timeZoneOffsetValue))
                //    timeZoneOffset = timeZoneOffsetValue;

                if (value.TryGetValue("startTime", out DateTime startTimeValue))
                    startTime = startTimeValue;

                if (value.TryGetValue("endTime", out DateTime endTimeValue))
                    endTime = endTimeValue;

                if (value.TryGetValue("detectionStartTime", out DateTime detectionStartTimeValue))
                    detectionStartTime = detectionStartTimeValue;

                if (value.TryGetValue("detectionEndTime", out DateTime detectionEndTimeValue))
                    detectionEndTime = detectionEndTimeValue;

                if (value.TryGetValue("closePositionsAtTime", out DateTime closePositionsAtTimeValue))
                    closePositionsAtTime = closePositionsAtTimeValue;

                if (value.TryGetValue("minimumRangeSize", out int minimumRangeSizeValue))
                    minimumRangeSize = minimumRangeSizeValue;

                if (value.TryGetValue("firstEntryOption", out FirstEntryOption firstEntryOptionValue))
                    firstEntryOption = firstEntryOptionValue;

                if (value.TryGetValue("takeProfitType", out TPType takeProfitTypeValue))
                    takeProfitType = takeProfitTypeValue;

                if (value.TryGetValue("initialQuantity", out int initialQuantityValue))
                    initialQuantity = initialQuantityValue;

                if (value.TryGetValue("stopLossPercentage", out double stopLossPercentageValue))
                    stopLossPercentage = stopLossPercentageValue;

                if (value.TryGetValue("enableBreakEven", out bool enableBreakEvenValue))
                    enableBreakEven = enableBreakEvenValue;

                if (value.TryGetValue("takeProfitPercentage", out double takeProfitPercentageValue))
                    takeProfitPercentage = takeProfitPercentageValue;

                if (value.TryGetValue("takeProfitPoints", out double takeProfitPointsValue))
                    takeProfitPoints = takeProfitPointsValue;

                if (value.TryGetValue("breakevenOption", out BreakevenOption breakevenOptionValue))
                    breakevenOption = breakevenOptionValue;

                if (value.TryGetValue("tpAdjustmentType", out TpAdjustmentType tpAdjustmentTypeValue))
                    tpAdjustmentType = tpAdjustmentTypeValue;

                if (value.TryGetValue("tpAdjustmentValue", out double tpAdjustmentValueValue))
                    tpAdjustmentValue = tpAdjustmentValueValue;

                // DCA Levels
                SetDcaSettings(value);
            }
        }

        private IList<SettingItem> GetDcaSettings()
        {
            var settings = new List<SettingItem>();

            // DCA Level 1
            settings.Add(new SettingItemBoolean("enableDcaLevel1", enableDcaLevel1)
            {
                Text = "Enable DCA Level 1",
                SortIndex = 100
            });

            settings.Add(new SettingItemDouble("dcaPercentage1", dcaPercentage1)
            {
                Text = "DCA Level 1 Trigger Percentage",
                SortIndex = 101,
                Minimum = 0.01,
                Maximum = 5,
                Increment = 0.01,
                DecimalPlaces = 2,
                Relation = new SettingItemRelationEnability("enableDcaLevel1", true)
            });

            settings.Add(new SettingItemInteger("dcaQuantity1", dcaQuantity1)
            {
                Text = "DCA Level 1 Quantity",
                SortIndex = 102,
                Relation = new SettingItemRelationEnability("enableDcaLevel1", true)
            });

            // DCA Level 2
            settings.Add(new SettingItemBoolean("enableDcaLevel2", enableDcaLevel2)
            {
                Text = "Enable DCA Level 2",
                SortIndex = 110
            });

            settings.Add(new SettingItemDouble("dcaPercentage2", dcaPercentage2)
            {
                Text = "DCA Level 2 Trigger Percentage",
                SortIndex = 111,
                Minimum = 0.01,
                Maximum = 5,
                Increment = 0.01,
                DecimalPlaces = 2,
                Relation = new SettingItemRelationEnability("enableDcaLevel2", true)
            });

            settings.Add(new SettingItemInteger("dcaQuantity2", dcaQuantity2)
            {
                Text = "DCA Level 2 Quantity",
                SortIndex = 112,
                Relation = new SettingItemRelationEnability("enableDcaLevel2", true)
            });

            // DCA Level 3
            settings.Add(new SettingItemBoolean("enableDcaLevel3", enableDcaLevel3)
            {
                Text = "Enable DCA Level 3",
                SortIndex = 130
            });

            settings.Add(new SettingItemDouble("dcaPercentage3", dcaPercentage3)
            {
                Text = "DCA Level 3 Trigger Percentage",
                SortIndex = 131,
                Minimum = 0.01,
                Maximum = 5,
                Increment = 0.01,
                DecimalPlaces = 2,
                Relation = new SettingItemRelationEnability("enableDcaLevel3", true)
            });

            settings.Add(new SettingItemInteger("dcaQuantity3", dcaQuantity3)
            {
                Text = "DCA Level 3 Quantity",
                SortIndex = 132,
                Relation = new SettingItemRelationEnability("enableDcaLevel3", true)
            });

            return settings;
        }

        private void SetDcaSettings(IList<SettingItem> value)
        {
            if (value.TryGetValue("enableDcaLevel1", out bool enableDcaLevel1Value))
                enableDcaLevel1 = enableDcaLevel1Value;

            if (value.TryGetValue("dcaPercentage1", out double dcaPercentage1Value))
                dcaPercentage1 = dcaPercentage1Value;

            if (value.TryGetValue("dcaQuantity1", out int dcaQuantity1Value))
                dcaQuantity1 = dcaQuantity1Value;

            if (value.TryGetValue("enableDcaLevel2", out bool enableDcaLevel2Value))
                enableDcaLevel2 = enableDcaLevel2Value;

            if (value.TryGetValue("dcaPercentage2", out double dcaPercentage2Value))
                dcaPercentage2 = dcaPercentage2Value;

            if (value.TryGetValue("dcaQuantity2", out int dcaQuantity2Value))
                dcaQuantity2 = dcaQuantity2Value;

            if (value.TryGetValue("enableDcaLevel3", out bool enableDcaLevel3Value))
                enableDcaLevel3 = enableDcaLevel3Value;

            if (value.TryGetValue("dcaPercentage3", out double dcaPercentage3Value))
                dcaPercentage3 = dcaPercentage3Value;

            if (value.TryGetValue("dcaQuantity3", out int dcaQuantity3Value))
                dcaQuantity3 = dcaQuantity3Value;
        }
    }
}
