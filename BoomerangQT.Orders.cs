using System;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.LocalOrders;

namespace BoomerangQT
{
    public partial class BoomerangQT
    {
        private void PlaceSelectedEntry(Side? side = null)
        {
            try
            {
                // Determine side if not provided (based on breakout direction or default)
                if (!side.HasValue)
                {
                    side = default(Side); // Set a default value if necessary
                }

                switch (firstEntryOption)
                {
                    case FirstEntryOption.MainEntry:
                        PlaceTrade(side.Value);
                        break;
                    case FirstEntryOption.DcaLevel1:
                        PlaceFirstDcaEntry(1, side.Value);
                        break;
                    case FirstEntryOption.DcaLevel2:
                        PlaceFirstDcaEntry(2, side.Value);
                        break;
                    case FirstEntryOption.DcaLevel3:
                        PlaceFirstDcaEntry(3, side.Value);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"Exception in PlaceSelectedEntry: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void PlaceTrade(Side side)
        {
            try
            {
                Log($"Placing {side} trade.", StrategyLoggingLevel.Trading);

                // Ensure initialQuantity is above 0, otherwise use 1
                var quantityToUse = initialQuantity > 0 ? initialQuantity : 1;

                var result = Core.Instance.PlaceOrder(new PlaceOrderRequestParameters
                {
                    Symbol = symbol,
                    Account = account,
                    Side = side,
                    OrderTypeId = OrderType.Market,
                    Quantity = quantityToUse,
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

        private void PlaceFirstDcaEntry(int levelNumber, Side side)
        {
            try
            {
                var dcaLevel = dcaLevels.FirstOrDefault(d => d.LevelNumber == levelNumber);
                if (dcaLevel == null)
                {
                    Log($"DCA Level {levelNumber} is not enabled.", StrategyLoggingLevel.Error);
                    return;
                }

                dcaLevel.IsFirstEntry = true;

                Log($"Placing first entry at DCA Level {levelNumber} with quantity {dcaLevel.Quantity}.", StrategyLoggingLevel.Trading);

                var result = Core.Instance.PlaceOrder(new PlaceOrderRequestParameters
                {
                    Symbol = symbol,
                    Account = account,
                    Side = side,
                    OrderTypeId = OrderType.Market,
                    Quantity = dcaLevel.Quantity,
                });

                if (result.Status == TradingOperationResultStatus.Failure)
                    Log($"Failed to place DCA Level {levelNumber} entry: {result.Message}", StrategyLoggingLevel.Error);
                else
                    Log($"DCA Level {levelNumber} entry placed successfully.", StrategyLoggingLevel.Trading);
            }
            catch (Exception ex)
            {
                Log($"Exception in PlaceFirstDcaEntry: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void OnPositionAdded(Position position)
        {
            try
            {
                if (position.Symbol == null || symbol == null || !position.Symbol.Name.StartsWith(symbol.Name) || position.Account != account)
                    return;

                currentPosition = position;

                Log($"New position added. Side: {position.Side}, Quantity: {position.Quantity}, Open Price: {position.OpenPrice}", StrategyLoggingLevel.Trading);

                if (strategyStatus == Status.ManagingTrade || enableMarketReplayMode)
                {
                    ProtectPosition();
                }
            }
            catch (Exception ex)
            {
                Log($"Exception in OnPositionAdded: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void ProtectPosition()
        {
            try
            {
                Log("Protecting position with SL, TP, and DCA.", StrategyLoggingLevel.Trading);

                // Place SL with total potential position size
                PlaceOrUpdateStopLoss(totalQuantity: GetTotalPotentialPositionSize());

                // Place initial TP
                PlaceOrUpdateTakeProfit();

                // Place DCA orders
                PlaceDcaOrders();
            }
            catch (Exception ex)
            {
                Log($"Exception in ProtectPosition: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void PlaceOrUpdateStopLoss(double totalQuantity)
        {
            try
            {
                if (currentPosition == null) return;

                var stopLossPrice = CalculateStopLossPrice();

                var request = new PlaceOrderRequestParameters
                {
                    Symbol = symbol,
                    Account = account,
                    Side = currentPosition.Side.Invert(),
                    OrderTypeId = "Stop",
                    Quantity = totalQuantity,
                    TriggerPrice = stopLossPrice,
                    PositionId = currentPosition.Id,
                    AdditionalParameters = new List<SettingItem>
                    {
                        new SettingItemBoolean(OrderType.REDUCE_ONLY, true)
                    }
                };

                CancelExistingOrder(stopLossOrderId);

                var result = Core.Instance.PlaceOrder(request);
                if (result.Status == TradingOperationResultStatus.Failure)
                    Log($"Failed to place Stop Loss order: {result.Message}", StrategyLoggingLevel.Error);
                else
                {
                    Log($"Stop Loss order placed at {stopLossPrice} with quantity {totalQuantity}.", StrategyLoggingLevel.Trading);
                    stopLossOrderId = result.OrderId;
                }
            }
            catch (Exception ex)
            {
                Log($"Exception in PlaceOrUpdateStopLoss: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void PlaceOrUpdateTakeProfit()
        {
            try
            {
                if (currentPosition == null) return;

                var takeProfitPrice = CalculateTakeProfitPrice();

                var request = new PlaceOrderRequestParameters
                {
                    Symbol = symbol,
                    Account = account,
                    Side = currentPosition.Side.Invert(),
                    OrderTypeId = "Limit",
                    Quantity = currentPosition.Quantity,
                    Price = takeProfitPrice,
                    PositionId = currentPosition.Id,
                    AdditionalParameters = new List<SettingItem>
                    {
                        new SettingItemBoolean(OrderType.REDUCE_ONLY, true)
                    }
                };

                CancelExistingOrder(takeProfitOrderId);

                var result = Core.Instance.PlaceOrder(request);
                if (result.Status == TradingOperationResultStatus.Failure)
                    Log($"Failed to place Take Profit order: {result.Message}", StrategyLoggingLevel.Error);
                else
                {
                    Log($"Take Profit order placed at {takeProfitPrice} with quantity {currentPosition.Quantity}.", StrategyLoggingLevel.Trading);
                    takeProfitOrderId = result.OrderId;
                }
            }
            catch (Exception ex)
            {
                Log($"Exception in PlaceOrUpdateTakeProfit: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private double CalculateStopLossPrice()
        {
            try
            {
                Log($"Calculating Stop Loss Price. OpenPrice: {currentPosition.OpenPrice}, Side: {currentPosition.Side}, StopLossPercentage: {stopLossPercentage}");

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
                double takeProfitPrice = currentPosition.OpenPrice;

                switch (takeProfitType)
                {
                    case TPType.OppositeSideOfRange:
                        if (rangeHigh.HasValue && rangeLow.HasValue)
                        {
                            takeProfitPrice = currentPosition.Side == Side.Buy ? rangeHigh.Value : rangeLow.Value;
                            Log($"Take Profit set to opposite side of range: {takeProfitPrice}", StrategyLoggingLevel.Trading);
                        }
                        else
                        {
                            Log("Range values are not set. Cannot calculate TP based on opposite side of range.", StrategyLoggingLevel.Error);
                        }
                        break;

                    case TPType.FixedPercentage:
                        double tpPercentage = takeProfitPercentage / 100;
                        takeProfitPrice = currentPosition.Side == Side.Buy
                            ? currentPosition.OpenPrice + currentPosition.OpenPrice * tpPercentage
                            : currentPosition.OpenPrice - currentPosition.OpenPrice * tpPercentage;
                        Log($"Take Profit set using fixed percentage: {takeProfitPrice}", StrategyLoggingLevel.Trading);
                        break;

                    case TPType.FixedPoints:
                        takeProfitPrice = currentPosition.Side == Side.Buy
                            ? currentPosition.OpenPrice + takeProfitPoints
                            : currentPosition.OpenPrice - takeProfitPoints;
                        Log($"Take Profit set using fixed points: {takeProfitPrice}", StrategyLoggingLevel.Trading);
                        break;
                }

                // Adjust for breakeven if conditions are met
                if (enableBreakEven && numberDCA >= numberDCAToBE)
                {
                    double breakevenPrice = currentPosition.OpenPrice + (currentPosition.Side == Side.Buy ? breakevenPlusPoints : -breakevenPlusPoints);
                    takeProfitPrice = breakevenPrice;
                    Log($"Breakeven conditions met. Adjusted Take Profit to breakeven price: {takeProfitPrice}", StrategyLoggingLevel.Trading);
                }

                return takeProfitPrice;
            }
            catch (Exception ex)
            {
                Log($"Exception in CalculateTakeProfitPrice: {ex.Message}", StrategyLoggingLevel.Error);
                throw;
            }
        }

        private void CancelExistingOrder(string orderId)
        {
            try
            {
                if (!string.IsNullOrEmpty(orderId))
                {
                    // Retrieve the Order object using the orderId
                    var existingOrder = Core.Instance.GetOrderById(orderId);

                    if (existingOrder != null && existingOrder.Status == OrderStatus.Filled)
                    {
                        Log($"Cancelling existing order ID: {existingOrder.Id}", StrategyLoggingLevel.Trading);

                        var cancelResult = Core.Instance.CancelOrder(existingOrder);
                        if (cancelResult.Status == TradingOperationResultStatus.Failure)
                            Log($"Failed to cancel existing order: {cancelResult.Message}", StrategyLoggingLevel.Error);
                        else
                        {
                            Log("Existing order cancelled successfully.", StrategyLoggingLevel.Trading);

                            // Reset the reference
                            if (orderId == stopLossOrderId)
                                stopLossOrderId = null;
                            else if (orderId == takeProfitOrderId)
                                takeProfitOrderId = null;
                        }
                    }
                    else
                    {
                        Log($"Order with ID {orderId} not found or not active.", StrategyLoggingLevel.Trading);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Exception in CancelExistingOrder: {ex.Message}", StrategyLoggingLevel.Error);
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

                CancelAssociatedOrders();
            }
            catch (Exception ex)
            {
                Log($"Exception in ClosePosition: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void CancelAssociatedOrders()
        {
            try
            {
                // Cancel Stop Loss order
                CancelExistingOrder(stopLossOrderId);

                // Cancel Take Profit order
                CancelExistingOrder(takeProfitOrderId);

                // Reset the orderId references
                stopLossOrderId = null;
                takeProfitOrderId = null;

                // Cancel DCA orders
                CancelDcaOrders();
            }
            catch (Exception ex)
            {
                Log($"Exception in CancelAssociatedOrders: {ex.Message}", StrategyLoggingLevel.Error);
            }
        }

        private void OnPositionRemoved(Position position)
        {
            try
            {
                if (position.Symbol == null || symbol == null || !position.Symbol.Name.StartsWith(symbol.Name) || position.Account != account) return;
                if (currentPosition == null) return;
                if (currentPosition.Id != position.Id) return;

                Log($"Position {position.Id} has been closed.", StrategyLoggingLevel.Trading);

                // Cancel any remaining orders associated with the position
                CancelAssociatedOrders();

                currentPosition = null;

                ResetStrategy();
            }
            catch (Exception ex)
            {
                Log($"Exception in OnPositionRemoved: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void OnOrderUpdated(object sender, LocalOrderEventArgs e)
        {
            try
            {
                var order = e.LocalOrder;

                if (dcaLevels.Any(d => d.OrderId == order.Id && order.IsFilled()))
                {
                    CheckDcaExecutions();
                }
            }
            catch (Exception ex)
            {
                Log($"Exception in OnOrderUpdated: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private double GetTotalPotentialPositionSize()
        {
            int totalQuantity = initialQuantity + dcaLevels.Sum(d => d.IsFirstEntry ? 0 : d.Quantity);

            // Add the quantity of the first entry level if it's a DCA level
            if (firstEntryOption != FirstEntryOption.MainEntry)
            {
                var firstDcaLevel = dcaLevels.FirstOrDefault(d => d.LevelNumber == (int)firstEntryOption);
                if (firstDcaLevel != null)
                {
                    totalQuantity += firstDcaLevel.Quantity;
                }
            }

            return totalQuantity;
        }
    }
}
