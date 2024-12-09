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
                    //Log($"Side not set", StrategyLoggingLevel.Error);
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
                        break;
                    case FirstEntryOption.DcaLevel2:
                        PlaceDCALimitOrder(1, side.Value); // This will be a simple limit order, later will be protected by the event that checks when number of contracts used variate
                        strategyStatus = Status.WaitingToEnter;
                        break;
                    case FirstEntryOption.DcaLevel3:
                        PlaceDCALimitOrder(1, side.Value); // This will be a simple limit order, later will be protected by the event that checks when number of contracts used variate
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
            Log($"PlaceTrade", StrategyLoggingLevel.Trading);
            try
            {
                //Log($"Placing {side} trade.", StrategyLoggingLevel.Trading);

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

                expectedContracts = quantityToUse;

                if (result.Status == TradingOperationResultStatus.Failure)
                    Log($"Failed to place order: {result.Message}", StrategyLoggingLevel.Error);
                else
                {
                    //Log("Order placed successfully.", StrategyLoggingLevel.Trading);
                }
            }
            catch (Exception ex)
            {
                Log($"Exception in PlaceTrade: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        private void PlaceDCALimitOrder(int levelNumber, Side side)
        {
            Log($"PlaceDCALimitOrder levelNumber: {levelNumber}");
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

                //Log($"Placing first entry at DCA Level {levelNumber} with quantity {dcaLevel.Quantity}.", StrategyLoggingLevel.Trading);

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
                {
                    //Log($"Failed to place DCA Level {levelNumber} entry: {result.Message}", StrategyLoggingLevel.Error);
                    Stop();
                }
                else
                {
                    //Log($"DCA Level {levelNumber} entry placed successfully.", StrategyLoggingLevel.Trading);
                    expectedContracts = dcaLevel.Quantity;
                    strategyStatus = Status.WaitingToEnter;
                }
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
                Log($"OnPositionAdded", StrategyLoggingLevel.Trading);

                //Log($"status: {strategyStatus} - WaitingForRange or ManagingTrade will make us exit this function");

                if (strategyStatus == Status.WaitingForRange || strategyStatus == Status.ManagingTrade)
                {
                    // We only want to enter this function if we're waiting for a breakout or we're waiting to enter a DCA first entry
                    // I think sometimes during intermediate DCA entries the new merged position also calls this function, we don't want to do anything then because 
                    // for us is just the same position with increased contracts

                    //Log($"Exiting function OnPositionAdded");
                    //Log($"Position: {position}");
                    return;
                }

                Log($"currentPosition: {currentPosition} - only null will make us continue");

                // if a position is being opened: by range breakout when main entry is active or by an execution of a DCA order
                // This will only happen once per range
                if (currentPosition != null) return;

                Log($"position.Symbol: {position.Symbol} and currentSymbol: {CurrentSymbol} should be the same, and the accounts also {position.Account}/{CurrentAccount}");

                if (position.Symbol == null || CurrentSymbol == null || !position.Symbol.Name.StartsWith(CurrentSymbol.Name) || position.Account != CurrentAccount) return;

                currentPosition = position;

                if (currentPosition != null)
                {
                    currentPosition.Updated -= OnPositionUpdated; // Remove any existing subscription
                    currentPosition.Updated += OnPositionUpdated; // Add a fresh subscription
                    Log("Subscribed to OnPositionUpdated for the new currentPosition.", StrategyLoggingLevel.Trading);

                    // Reset maxDrawdown when a new position is established
                    // At the start, PnL is essentially at 0 (no negative excursion yet)
                    maxDrawdown = 0.0;
                }


                //Log($"CurrentPosition is set");

                //Log($"New position added. Side: {position.Side}, Quantity: {position.Quantity}, Open Price: {position.OpenPrice}", StrategyLoggingLevel.Trading);


            }
            catch (Exception ex)
            {
                Log($"Exception in OnPositionAdded: {ex.Message}", StrategyLoggingLevel.Error);
                Stop();
            }
        }

        // This function basically serves the purpose when going up with amount of contracts
        // I believe so far that the issue is in entering order not touching SL or TP which decrease the number of contracts
        private void OnPositionUpdated(Position position)
        {
            //Log($"OnPositionUpdated, locked semaphore: {this.semaphoreOnPositionUpdated}", StrategyLoggingLevel.Trading);

            if (position.Symbol == null || CurrentSymbol == null || !position.Symbol.Name.StartsWith(CurrentSymbol.Name) || position.Account != CurrentAccount) return;

            if (this.semaphoreOnPositionUpdated == true) return;

            this.semaphoreOnPositionUpdated = true;

            //Log($"Position updated: {position}");

            // We need to identify moments to skip this checking, because we only want to protect the complete position
            if (currentPosition.Quantity < expectedContracts)
            {
                this.semaphoreOnPositionUpdated = false; // Release the lock
                return; // Contracts are probably being added to the position, not complete yet, this will also accept adding contracts via DCA orders
            }

            // If expected contracts are not smaller and probably the same as the current positions contracts, we understand that the position is complete and we can continue

            if (strategyStatus == Status.BreakoutDetection || strategyStatus == Status.WaitingToEnter) // This will make sure that position only gets protected and dca placed once
            {
                ProtectPosition(); // Whatever happens if position is complete we should protect it
                PlaceDcaOrders(); // The ones not placed yet, this needs to happen only the first time the position is opened and completed

                currentContractsUsed = currentPosition.Quantity;

                // If main entry is selected the initial DCA will be zero because no DCA level has been utilized, otherwise we have used the first one (always)
                if (firstEntryOption == FirstEntryOption.MainEntry)
                {
                    executedDCALevel = 0;
                }
                else
                {
                    executedDCALevel = 1;
                }

                // In case we have defined DCA levels we need to check if there is something else we are expecting to hit
                // In other words, there could be one or more DCA levels still pending
                if (dcaLevels.Count > 0)
                {
                    // We should increment quantityToManage with next DCA level to potentially get hit
                    var nextDCALevel = executedDCALevel + 1;

                    var highestLevel = dcaLevels.OrderByDescending(d => d.LevelNumber).FirstOrDefault();

                    //Log($"executedDCALevel: {executedDCALevel}");
                    //Log($"nextDCALevel: {nextDCALevel}");

                    // If there are more DCA levels, we check if the next one is actually below or equal to the highest
                    if (nextDCALevel <= highestLevel.LevelNumber)
                    {
                        foreach (var dcaLevel in dcaLevels)
                        {
                            //Log($"debugging stuff: { dcaLevel }");
                        }

                        var nextLevel = dcaLevels.FirstOrDefault(d => d.LevelNumber == nextDCALevel);
                        //Log($"nextLevel: {nextLevel}");
                        //Log($"nextLevel.Quantity: {nextLevel.Quantity}");

                        expectedContracts += nextLevel.Quantity;

                        //Log($"Number of expected contrats updated to: {expectedContracts}");
                    } else
                    {
                        // If next DCA level is higher than the highest means, we are done expecting DCA levels
                        expectedContracts = 0;
                    }
                } else
                {
                    expectedContracts = 0;
                }

                // Whatever happens with the expected contracts we're now managing a trade
                strategyStatus = Status.ManagingTrade;
                //Log($"StrategyStatus is moved to: {strategyStatus}");
            }
            else if (strategyStatus == Status.ManagingTrade)
            {
                if (currentPosition.Quantity > currentContractsUsed)
                {
                    // We just started to hit an iceberg (DCA level) don't do anything yet
                }

                if (currentPosition.Quantity == expectedContracts)
                {
                    //Log($"We just hit the ICEBERG, DCA number: {executedDCALevel}");
                    ProtectPosition(); // Whatever happens if position is complete we should protect it

                    currentContractsUsed = currentPosition.Quantity;

                    if (executedDCALevel < dcaLevels.Count) // For example if we're at level 2 but we know there are 3 levels
                    {
                        executedDCALevel++;

                        // We should increment quantityToManage with next DCA level to potentially get hit
                        var nextDCALevel = executedDCALevel + 1;

                        var highestLevel = dcaLevels.OrderByDescending(d => d.LevelNumber).FirstOrDefault();

                        if (nextDCALevel <= highestLevel.LevelNumber)
                        {
                            var nextLevel = dcaLevels.FirstOrDefault(d => d.LevelNumber == nextDCALevel);
                            expectedContracts += nextLevel.Quantity;
                            //Log($"Number of expected contrats updated to: {expectedContracts}");
                        }
                        else
                        {
                            expectedContracts = 0; // We don't expect more contracts anymore
                        }
                    } else
                    {
                        expectedContracts = 0;
                    }
                }

                // Is fair to assume that if there are not expected contracts to reach we're just waiting for the position to close at SL or TP at this point
            }

            this.semaphoreOnPositionUpdated = false;
        }

        private void ProtectPosition()
        {
            try
            {
                Log("ProtectPosition", StrategyLoggingLevel.Trading);
                
                // Place SL with total potential position size
                PlaceOrUpdateStopLoss();

                // Place initial TP
                PlaceOrUpdateTakeProfit();

                //Log("Position PROTECTED", StrategyLoggingLevel.Trading);
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

                //Log($"Stop Loss price: {stopLossPrice}", StrategyLoggingLevel.Trading);

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
                    //Log($"Stop Loss order placed at {stopLossPrice} with quantity {currentPosition.Quantity}.", StrategyLoggingLevel.Trading);
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

                //Log($"takeProfitPrice ---: {takeProfitPrice}", StrategyLoggingLevel.Trading);

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

                //Log($"Cancelling previous takeProfitOrderId {takeProfitOrderId}", StrategyLoggingLevel.Trading);
                CancelExistingOrder(takeProfitOrderId);

                var result = Core.Instance.PlaceOrder(request);
                if (result.Status == TradingOperationResultStatus.Failure)
                    Log($"Failed to place Take Profit order: {result.Message}", StrategyLoggingLevel.Error);
                else
                {
                    //Log($"Take Profit order placed at {takeProfitPrice} with quantity {currentPosition.Quantity}.", StrategyLoggingLevel.Trading);
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

                //Log($"Calculating Stop Loss Price. OpenPrice: {this.openPrice}, Side: {strategySide}, StopLossPercentage: {stopLossPercentage}");

                double stopLossPrice = strategySide == Side.Buy
                    ? (double) this.openPrice - (double) this.openPrice * (stopLossPercentage / 100)
                    : (double) this.openPrice + (double) this.openPrice * (stopLossPercentage / 100);

                //Log($"Calculated Stop Loss Price: {stopLossPrice}", StrategyLoggingLevel.Trading);

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
                //Log($"initial takeprofit price: {takeProfitPrice}");

                //Log($"enableBreakEven: {enableBreakEven}");
                //Log($"breakevenOption: {breakevenOption}");
                //Log($"numberDCA: {numberDCA}");
              
                // Adjust for breakeven if conditions are met, basically we enter BE functionality if DCA is equal to the configured one, or if the configured option in "every dca level"
                if ((enableBreakEven && breakevenOption == BreakevenOption.EveryDcaLevel) || (enableBreakEven && (int) breakevenOption == executedDCALevel))
                {
                    double breakevenPrice = currentPosition.OpenPrice;

                    // Adjust the TP according to the TP adjustment settings
                    double adjustment = 0.0;

                    //Log($"tpAdjustmentType: {tpAdjustmentType}");

                    //Log($"(TpAdjustmentType)tpAdjustmentType: {(TpAdjustmentType)tpAdjustmentType}");

                    switch ((TpAdjustmentType)tpAdjustmentType)
                    {
                        case TpAdjustmentType.FixedPoints:
                            adjustment = tpAdjustmentValue;
                            break;

                        case TpAdjustmentType.FixedPercentage:
                            
                            adjustment = breakevenPrice * (tpAdjustmentValue / 100.0);
                            break;

                        case TpAdjustmentType.RangeSize:
                            if (rangeHigh.HasValue && rangeLow.HasValue)
                            {
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

                    //Log($"Type of Breakeven: {(TpAdjustmentType)tpAdjustmentType}", StrategyLoggingLevel.Trading);
                    //Log($"Breakeven value: {adjustment}", StrategyLoggingLevel.Trading);
                    //Log($"New calculated Breakeven Price: {breakevenPrice}", StrategyLoggingLevel.Trading);
                    takeProfitPrice = breakevenPrice;
                   // Log($"Breakeven conditions met. Adjusted Take Profit to breakeven price: {takeProfitPrice}", StrategyLoggingLevel.Trading);

                    return takeProfitPrice;
                }

                switch (takeProfitType)
                {
                    case TPType.OppositeSideOfRange:
                        if (rangeHigh.HasValue && rangeLow.HasValue)
                        {
                            takeProfitPrice = currentPosition.Side == Side.Buy ? rangeHigh.Value : rangeLow.Value;
                            //Log($"Take Profit set to opposite side of range: {takeProfitPrice}", StrategyLoggingLevel.Trading);
                        }
                        else
                        {
                            //Log("Range values are not set. Cannot calculate TP based on opposite side of range.", StrategyLoggingLevel.Error);
                        }
                        break;

                    case TPType.FixedPercentage:
                        double tpPercentage = takeProfitPercentage / 100;
                        takeProfitPrice = currentPosition.Side == Side.Buy
                            ? currentPosition.OpenPrice + (currentPosition.OpenPrice * tpPercentage)
                            : currentPosition.OpenPrice - (currentPosition.OpenPrice * tpPercentage);
                        //Log($"Take Profit set using fixed percentage: {takeProfitPrice}", StrategyLoggingLevel.Trading);
                        break;

                    case TPType.FixedPoints:
                        takeProfitPrice = currentPosition.Side == Side.Buy
                            ? currentPosition.OpenPrice + takeProfitPoints
                            : currentPosition.OpenPrice - takeProfitPoints;
                        //Log($"Take Profit set using fixed points: {takeProfitPrice}", StrategyLoggingLevel.Trading);
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
                        //Log($"Cancelling existing order ID: {existingOrder.Id}", StrategyLoggingLevel.Trading);

                        var cancelResult = Core.Instance.CancelOrder(existingOrder);
                        if (cancelResult.Status == TradingOperationResultStatus.Failure)
                            Log($"Failed to cancel existing order: {cancelResult.Message}", StrategyLoggingLevel.Error);
                        else
                        {
                            //Log("Existing order cancelled successfully.", StrategyLoggingLevel.Trading);

                            // Reset the reference
                            if (orderId == stopLossOrderId)
                                stopLossOrderId = null;
                            else if (orderId == takeProfitOrderId)
                                takeProfitOrderId = null;
                        }
                    }
                    else
                    {
                        //Log($"Order with ID {orderId} not found or not active.", StrategyLoggingLevel.Trading);
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

                //Log($"SL and TP removed succesfully");

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
            Log("OnPositionRemoved", StrategyLoggingLevel.Trading);

            try
            {
                if (position.Symbol == null || CurrentSymbol == null || !position.Symbol.Name.StartsWith(CurrentSymbol.Name) || position.Account != CurrentAccount) return;
                if (currentPosition == null) return;
                if (currentPosition.Id != position.Id) return;

                double closedPnL = position.GrossPnL.Value;
                double lastMaxDrawdown = maxDrawdown;

                // Llamada al método para enviar el email desde el otro archivo parcial
                SendTradeClosedEmail(position.Symbol.Name, closedPnL, lastMaxDrawdown);

                // Llamada al método para enviar mensaje a telegram desde el otro archivo parcial
                System.Threading.Tasks.Task tgTask = SendTradeClosedTelegramMsg(position.Symbol.Name, closedPnL, lastMaxDrawdown);

                //Log($"OnPositionRemove event called - Position {position.Id} has been closed.", StrategyLoggingLevel.Trading);

                // Cancel any remaining orders associated with the position
                CancelAssociatedOrders();

                currentPosition = null;
                position.Updated -= OnPositionUpdated;

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
