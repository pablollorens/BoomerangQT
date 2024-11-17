using System;
using System.Collections.Generic;
using System.Linq;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Integration;
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
                    Log($"Side not set", StrategyLoggingLevel.Error);
                    Stop();
                }

                strategySide = side.Value;

                switch (firstEntryOption)
                {
                    case FirstEntryOption.MainEntry:
                        PlaceTrade(side.Value); // This will be protected
                        //strategyStatus = Status.ManagingTrade;
                        break;
                    case FirstEntryOption.DcaLevel1:
                        PlaceDCALimitOrder(1, side.Value); // This will be a simple limit order, later will be protected by the event that checks when number of contracts used variate
                        strategyStatus = Status.WaitingToEnter;
                        break;
                    case FirstEntryOption.DcaLevel2:
                        PlaceDCALimitOrder(2, side.Value); // This will be a simple limit order, later will be protected by the event that checks when number of contracts used variate
                        numberDCA = 1; // This is like we took one DCA already, the 2nd one will come with the touch of the limit order we just placed
                        strategyStatus = Status.WaitingToEnter;
                        break;
                    case FirstEntryOption.DcaLevel3:
                        PlaceDCALimitOrder(3, side.Value); // This will be a simple limit order, later will be protected by the event that checks when number of contracts used variate
                        numberDCA = 2; // This is like we took two DCAs already, the 3rd one will come with the touch of the limit order we just placed
                        strategyStatus = Status.WaitingToEnter;
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
                    Symbol = CurrentSymbol,
                    Account = CurrentAccount,
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

        private void PlaceDCALimitOrder(int levelNumber, Side side)
        {
            try
            {
                var dcaLevel = dcaLevels.FirstOrDefault(d => d.LevelNumber == levelNumber);

                foreach (var level in dcaLevels)
                {
                    Log($"DCALevel: {level}", StrategyLoggingLevel.Trading);
                }

                if (dcaLevel == null)
                {
                    Log($"DCA Level {levelNumber} is not enabled.", StrategyLoggingLevel.Error);
                    return;
                }

                dcaLevel.IsFirstEntry = true;

                Log($"Placing first entry at DCA Level {levelNumber} with quantity {dcaLevel.Quantity}.", StrategyLoggingLevel.Trading);

                var result = Core.Instance.PlaceOrder(new PlaceOrderRequestParameters
                {
                    Symbol = CurrentSymbol,
                    Account = CurrentAccount,
                    Side = side,
                    OrderTypeId = OrderType.Limit,
                    Price = CalculateDcaPrice(dcaLevel.TriggerPercentage),
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
                //Log($"OnPositionAdded");
                //Log($"status: {strategyStatus} - waiting for range or managing trade will make us exit");

                if (strategyStatus == Status.WaitingForRange || strategyStatus == Status.ManagingTrade) return;

                //Log($"currentPosition: {currentPosition} - only null will make us continue");

                // if a position is being opened: by range breakout when main entry is active, manually (if enabled) or by an execution of a DCA order
                // This will only happen once per range
                if (currentPosition != null) return;

                //Log($"position.Symbol: {position.Symbol} and currentSymbol: {CurrentSymbol} should be the same, and the accounts also {position.Account}/{CurrentAccount}");

                if (position.Symbol == null || CurrentSymbol == null || !position.Symbol.Name.StartsWith(CurrentSymbol.Name) || position.Account != CurrentAccount) return;

                currentPosition = position;
                //Log($"CurrentPosition is set");

                //Log($"New position added. Side: {position.Side}, Quantity: {position.Quantity}, Open Price: {position.OpenPrice}", StrategyLoggingLevel.Trading);

                if (strategyStatus == Status.BreakoutDetection || strategyStatus == Status.WaitingToEnter)
                {
                    strategyStatus = Status.ManagingTrade;

                    ProtectPosition(); // This needs to happen only the first time the position is opened
                    
                    // Place DCA orders
                    PlaceDcaOrders(); // The ones not placed yet

                    Log($"strategyStatus: {strategyStatus}");
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
                PlaceOrUpdateStopLoss();

                // Place initial TP
                PlaceOrUpdateTakeProfit();

                Log("Position PROTECTED", StrategyLoggingLevel.Trading);

                currentContractsUsed = currentPosition.Quantity;

                Log($"Current Contracts used: {currentContractsUsed}", StrategyLoggingLevel.Trading);
            }
            catch (Exception ex)
            {
                Log($"Exception in ProtectPosition: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void PlaceOrUpdateStopLoss()
        {
            try
            {
                if (currentPosition == null) return;

                var stopLossPrice = CalculateStopLossPrice();

                Log($"Stop Loss price: {stopLossPrice}", StrategyLoggingLevel.Trading);

                if (stopLossPrice == null)
                {
                    throw new Exception("Not possible to calculate global SL");
                }
                
                CancelExistingOrder(stopLossOrderId);

                var request = new PlaceOrderRequestParameters
                {
                    Symbol = CurrentSymbol,
                    Account = CurrentAccount,
                    Side = currentPosition.Side.Invert(),
                    OrderTypeId = "Stop",
                    Quantity = currentPosition.Quantity,
                    TriggerPrice = (double)stopLossPrice,
                    PositionId = currentPosition.Id,
                    AdditionalParameters = new List<SettingItem>
                    {
                        new SettingItemBoolean(OrderType.REDUCE_ONLY, true)
                    }
                };

                var result = Core.Instance.PlaceOrder(request);

                if (result.Status == TradingOperationResultStatus.Failure)
                    Log($"Failed to place Stop Loss order: {result.Message}", StrategyLoggingLevel.Error);
                else
                {
                    Log($"Stop Loss order placed at {stopLossPrice} with quantity {currentPosition.Quantity}.", StrategyLoggingLevel.Trading);
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

                Log($"takeProfitPrice ---: {takeProfitPrice}", StrategyLoggingLevel.Trading);

                var request = new PlaceOrderRequestParameters
                {
                    Symbol = CurrentSymbol,
                    Account = CurrentAccount,
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

                Log($"Cancelling previous takeProfitOrderId {takeProfitOrderId}", StrategyLoggingLevel.Trading);
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

        private double? CalculateStopLossPrice()
        {
            try
            {
                if (stopLossGlobalPrice != null) return stopLossGlobalPrice;

                Log($"Calculating Stop Loss Price. OpenPrice: {this.openPrice}, Side: {strategySide}, StopLossPercentage: {stopLossPercentage}");

                double stopLossPrice = strategySide == Side.Buy
                    ? (double) this.openPrice - (double) this.openPrice * (stopLossPercentage / 100)
                    : (double) this.openPrice + (double) this.openPrice * (stopLossPercentage / 100);

                Log($"Calculated Stop Loss Price: {stopLossPrice}", StrategyLoggingLevel.Trading);

                stopLossGlobalPrice = stopLossPrice;

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
                Log($"initial takeprofit price: {takeProfitPrice}");

                Log($"enableBreakEven: {enableBreakEven}");
                Log($"breakevenOption: {breakevenOption}");
                Log($"numberDCA: {numberDCA}");
              
                // Adjust for breakeven if conditions are met, basically we enter BE functionality if DCA is equal to the configured one, or if the configured option in "every dca level"
                if ((enableBreakEven && breakevenOption == BreakevenOption.EveryDcaLevel && numberDCA > 0) || (enableBreakEven && (int) breakevenOption == numberDCA))
                {
                    double breakevenPrice = currentPosition.OpenPrice;

                    // Adjust the TP according to the TP adjustment settings
                    double adjustment = 0.0;

                    Log($"tpAdjustmentType: {tpAdjustmentType}");

                    Log($"(TpAdjustmentType)tpAdjustmentType: {(TpAdjustmentType)tpAdjustmentType}");

                    switch ((TpAdjustmentType)tpAdjustmentType)
                    {
                        case TpAdjustmentType.FixedPoints:
                            Log($"we enter in BE 1 - fixedpoints");
                            adjustment = tpAdjustmentValue;
                            break;

                        case TpAdjustmentType.FixedPercentage:
                            Log($"we enter in BE 2 - FixedPercentage");
                            adjustment = breakevenPrice * (tpAdjustmentValue / 100.0);
                            break;

                        case TpAdjustmentType.RangeSize:
                            Log($"we enter in BE 3 - RangeSize");
                            if (rangeHigh.HasValue && rangeLow.HasValue)
                            {
                                Log($"we enter in BE 4 - RangeSize2");
                                adjustment = rangeHigh.Value - rangeLow.Value;
                            }
                            else
                            {
                                throw new Exception("Range values are not set. Cannot calculate TP adjustment based on Range Size.");
                            }
                            break;
                    }

                    if (currentPosition.Side == Side.Buy)
                    {
                        breakevenPrice = breakevenPrice + (double) adjustment;
                    }
                    else
                    {
                        breakevenPrice = breakevenPrice - (double) adjustment;
                    }

                    Log($"Current Price before BE calculation: {currentPosition.OpenPrice}", StrategyLoggingLevel.Trading);
                    Log($"Type of Breakeven: {(TpAdjustmentType)tpAdjustmentType}", StrategyLoggingLevel.Trading);
                    Log($"Breakeven value: {adjustment}", StrategyLoggingLevel.Trading);
                    Log($"New calculated Breakeven Price: {breakevenPrice}", StrategyLoggingLevel.Trading);
                    takeProfitPrice = breakevenPrice;
                    Log($"Breakeven conditions met. Adjusted Take Profit to breakeven price: {takeProfitPrice}", StrategyLoggingLevel.Trading);

                    return takeProfitPrice;
                }

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
                            ? currentPosition.OpenPrice + (currentPosition.OpenPrice * tpPercentage)
                            : currentPosition.OpenPrice - (currentPosition.OpenPrice * tpPercentage);
                        Log($"Take Profit set using fixed percentage: {takeProfitPrice}", StrategyLoggingLevel.Trading);
                        break;

                    case TPType.FixedPoints:
                        takeProfitPrice = currentPosition.Side == Side.Buy
                            ? currentPosition.OpenPrice + takeProfitPoints
                            : currentPosition.OpenPrice - takeProfitPoints;
                        Log($"Take Profit set using fixed points: {takeProfitPrice}", StrategyLoggingLevel.Trading);
                        break;
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

                    if (existingOrder != null)
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

                Log($"SL and TP removed succesfully");

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
                if (position.Symbol == null || CurrentSymbol == null || !position.Symbol.Name.StartsWith(CurrentSymbol.Name) || position.Account != CurrentAccount) return;
                if (currentPosition == null) return;
                if (currentPosition.Id != position.Id) return;

                Log($"OnPositionRemove event called - Position {position.Id} has been closed.", StrategyLoggingLevel.Trading);

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
    }
}
