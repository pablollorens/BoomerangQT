using System;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;

namespace BoomerangQT
{
    public partial class BoomerangQT
    {
        private void InitializeDcaLevels()
        {
            try
            {
                dcaLevels.Clear();

                // Add the DCA levels based on the selected first entry option
                if (firstEntryOption == FirstEntryOption.MainEntry)
                {
                    AddDcaLevel(1, enableDcaLevel1, dcaPercentage1, dcaQuantity1);
                    AddDcaLevel(2, enableDcaLevel2, dcaPercentage2, dcaQuantity2);
                    AddDcaLevel(3, enableDcaLevel3, dcaPercentage3, dcaQuantity3);
                }
                else if (firstEntryOption == FirstEntryOption.DcaLevel1)
                {
                    AddDcaLevel(1, enableDcaLevel1, dcaPercentage1, dcaQuantity1);
                    AddDcaLevel(2, enableDcaLevel2, dcaPercentage2, dcaQuantity2);
                    AddDcaLevel(3, enableDcaLevel3, dcaPercentage3, dcaQuantity3);
                }
                else if (firstEntryOption == FirstEntryOption.DcaLevel2)
                {
                    AddDcaLevel(1, enableDcaLevel2, dcaPercentage2, dcaQuantity2);
                    AddDcaLevel(2, enableDcaLevel3, dcaPercentage3, dcaQuantity3);
                }
                else if (firstEntryOption == FirstEntryOption.DcaLevel3)
                {
                    AddDcaLevel(1, enableDcaLevel3, dcaPercentage3, dcaQuantity3);
                }

                //Log("DCA levels initialized based on the first entry option.", StrategyLoggingLevel.Trading);

                // Sort DCA levels by trigger percentage in ascending order
                dcaLevels = dcaLevels.OrderBy(d => d.TriggerPercentage).ToList();

                // Rename the LevelNumber after sorting
                for (int i = 0; i < dcaLevels.Count; i++)
                {
                    dcaLevels[i].LevelNumber = i + 1; // Renumber starting from 1
                }

                // Print the activated DCA levels
                foreach (var dcaLevel in dcaLevels)
                {
                    //Log($"Activated DCA Level {dcaLevel.LevelNumber}: Trigger Percentage = {dcaLevel.TriggerPercentage}, Quantity = {dcaLevel.Quantity}", StrategyLoggingLevel.Trading);
                }
            }
            catch (Exception ex)
            {
                Log($"Exception in InitializeDcaLevels: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void AddDcaLevel(int levelNumber, bool isEnabled, double triggerPercentage, int quantity)
        {
            if (isEnabled)
            {
                dcaLevels.Add(new DcaLevel
                {
                    LevelNumber = levelNumber,
                    TriggerPercentage = triggerPercentage,
                    Quantity = quantity,
                    Executed = false
                });
                Log($"DCA Level {levelNumber} added: Trigger at {triggerPercentage}% with quantity {quantity}", StrategyLoggingLevel.Trading);
            }
        }

        private void PlaceDcaOrders()
        {
            Log("PlaceDCAOrders", StrategyLoggingLevel.Trading);
            try
            {
                foreach (var dcaLevel in dcaLevels)
                {
                    if (dcaLevel.IsFirstEntry)
                        continue; // Skip placing an order for the first entry level

                    double triggerPrice = CalculateDcaPrice(dcaLevel.TriggerPercentage);

                    var request = new PlaceOrderRequestParameters
                    {
                        Symbol = CurrentSymbol,
                        Account = CurrentAccount,
                        Side = currentPosition.Side,
                        OrderTypeId = "Limit",
                        Quantity = dcaLevel.Quantity,
                        Price = triggerPrice
                    };

                    var result = Core.Instance.PlaceOrder(request);
                    if (result.Status == TradingOperationResultStatus.Failure)
                    {
                        Log($"Failed to place DCA Level {dcaLevel.LevelNumber} order: {result.Message}", StrategyLoggingLevel.Error);
                    }
                    else
                    {
                        //Log($"Result of adding a DCA: {result}");
                        //Log($"DCA Level {dcaLevel.LevelNumber} order placed at {triggerPrice}.", StrategyLoggingLevel.Trading);
                        dcaLevel.OrderId = result.OrderId;
                        dcaOrders.Add(result.OrderId);
                        //Log($"DCA order list: {dcaOrders.Count}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Exception in PlaceDcaOrders: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private double CalculateDcaPrice(double percentage)
        {
            try
            {
                if (this.openPrice == null)
                {
                    throw new Exception("Breakout not detected");
                }

                double entryPrice = openPrice ?? 0.0;

                double dcaPrice = strategySide == Side.Buy
                ? entryPrice - entryPrice * (percentage / 100)
                : entryPrice + entryPrice * (percentage / 100);

                //Log($"Calculated DCA Price: {dcaPrice}", StrategyLoggingLevel.Trading);

                dcaPrice = CurrentSymbol.RoundPriceToTickSize(dcaPrice);

                return dcaPrice;
            }
            catch (Exception ex)
            {
                Log($"Exception in CalculateDcaPrice: {ex.Message}", StrategyLoggingLevel.Error);
                throw;
            }
        }

        private void CancelDcaOrders()
        {
            try
            {
                //Log($"dcaOrders: {dcaOrders}");
                foreach (var orderId in dcaOrders)
                {
                    //Log($"order: {orderId}");
                    var existingOrder = Core.Instance.GetOrderById(orderId);
                    if (existingOrder != null)
                    {
                        //Log($"existingOrder: {existingOrder}");
                        var cancelResult = Core.Instance.CancelOrder(existingOrder);
                        //Log($"cancelResult: {cancelResult}");
                        if (cancelResult.Status == TradingOperationResultStatus.Failure)
                        {
                            //Log($"Failed to cancel DCA order {orderId}: {cancelResult.Message}", StrategyLoggingLevel.Error);
                        }
                        else
                        {
                            //Log($"DCA order {orderId} cancelled.", StrategyLoggingLevel.Trading);
                        }
                    }
                }

                dcaOrders.Clear();
            }
            catch (Exception ex)
            {
                Log($"Exception in CancelDcaOrders: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private class DcaLevel
        {
            public int LevelNumber { get; set; }
            public double TriggerPercentage { get; set; }
            public int Quantity { get; set; }
            public bool Executed { get; set; } = false;
            public string OrderId { get; set; }
            public bool IsFirstEntry { get; set; } = false;
        }
    }
}
