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

        private void PlaceDcaOrders()
        {
            try
            {
                foreach (var dcaLevel in dcaLevels)
                {
                    if (dcaLevel.IsFirstEntry)
                        continue; // Skip placing an order for the first entry level

                    double triggerPrice = CalculateDcaPrice(dcaLevel.TriggerPercentage);

                    var request = new PlaceOrderRequestParameters
                    {
                        Symbol = symbol,
                        Account = account,
                        Side = currentPosition.Side,
                        OrderTypeId = "Limit",
                        Quantity = dcaLevel.Quantity,
                        Price = triggerPrice,
                        AdditionalParameters = new List<SettingItem>
                        {
                            new SettingItemBoolean(OrderType.REDUCE_ONLY, false)
                        }
                    };

                    var result = Core.Instance.PlaceOrder(request);
                    if (result.Status == TradingOperationResultStatus.Failure)
                    {
                        Log($"Failed to place DCA Level {dcaLevel.LevelNumber} order: {result.Message}", StrategyLoggingLevel.Error);
                    }
                    else
                    {
                        Log($"DCA Level {dcaLevel.LevelNumber} order placed at {triggerPrice}.", StrategyLoggingLevel.Trading);
                        dcaLevel.OrderId = result.OrderId;
                        dcaOrders.Add(Core.Instance.GetOrderById(result.OrderId));
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
                double dcaPrice = currentPosition.Side == Side.Buy
                    ? currentPosition.OpenPrice - currentPosition.OpenPrice * (percentage / 100)
                    : currentPosition.OpenPrice + currentPosition.OpenPrice * (percentage / 100);

                Log($"Calculated DCA Price: {dcaPrice}", StrategyLoggingLevel.Trading);

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
                foreach (var order in dcaOrders)
                {
                    var existingOrder = Core.Instance.GetOrderById(order.Id);
                    var cancelResult = Core.Instance.CancelOrder(existingOrder);
                    if (cancelResult.Status == TradingOperationResultStatus.Failure)
                    {
                        Log($"Failed to cancel DCA order {order.Id}: {cancelResult.Message}", StrategyLoggingLevel.Error);
                    }
                    else
                    {
                        Log($"DCA order {order.Id} cancelled.", StrategyLoggingLevel.Trading);
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

        private void CheckDcaExecutions()
        {
            try
            {
                foreach (var dcaLevel in dcaLevels.Where(d => !d.Executed && d.OrderId != null))
                {
                    var order = Core.Instance.GetOrderById(dcaLevel.OrderId);

                    if (order != null && order.IsFilled())
                    {
                        dcaLevel.Executed = true;
                        numberDCA++;
                        Log($"DCA Level {dcaLevel.LevelNumber} executed.", StrategyLoggingLevel.Trading);

                        // Update the Take Profit since position size has changed
                        PlaceOrUpdateTakeProfit();
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Exception in CheckDcaExecutions: {ex.Message}", StrategyLoggingLevel.Error);
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