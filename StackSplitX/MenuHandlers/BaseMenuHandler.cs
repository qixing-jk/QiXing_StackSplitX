using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using System;
using System.Diagnostics;
using StardewModdingAPI.Events;

namespace StackSplitX.MenuHandlers
{
    public abstract class BaseMenuHandler<TMenuType> 
        : IMenuHandler where TMenuType : IClickableMenu
    {
        /// <summary>The inventory handler.</summary>
        protected InventoryHandler Inventory;

        /// <summary>Split menu we display for the user to input the desired stack size.</summary>
        protected StackSplitMenu SplitMenu;

        /// <summary>Native game menu this handler is for.</summary>
        protected TMenuType NativeMenu { get; private set; }

        /// <summary>Mod helper.</summary>
        protected readonly IModHelper Helper;
        
        /// <summary>Monitor for logging.</summary>
        protected readonly IMonitor Monitor;

        /// <summary>Does this menu have an inventory section.</summary>
        protected bool HasInventory { get; set; } = true;

        /// <summary>Where the player clicked when the split menu was opened. 打开分割菜单时的点击位置</summary>
        protected Point ClickItemLocation { get; private set; }

        /// <summary>Tracks if the menu is currently open.</summary>
        private bool IsMenuOpen = false;

        /// <summary>处理状态</summary>
        public EInputHandled HandleStatus { get; set; }

        /// <summary>Constructs and instance.</summary>
        /// <param name="helper">Mod helper instance.</param>
        /// <param name="monitor">Monitor instance.</param>
        public BaseMenuHandler(IModHelper helper, IMonitor monitor)
        {
            this.Helper = helper;
            this.Monitor = monitor;
            this.Inventory = new InventoryHandler(helper.Reflection, monitor);
        }

        /// <summary>Checks if the menu this handler wraps is open.</summary>
        /// <returns>True if it is open, false otherwise.</returns>
        public virtual bool IsOpen()
        {
            return this.IsMenuOpen;
        }

        /// <summary>Checks the menu is the correct type.</summary>
        public bool IsCorrectMenuType(IClickableMenu menu)
        {
            return menu is TMenuType;
        }

        /// <summary>Notifies the handler that it's native menu has been opened.</summary>
        /// <param name="menu">The menu that was opened.</param>
        public virtual void Open(IClickableMenu menu)
        {
            Debug.Assert(IsCorrectMenuType(menu));
            this.NativeMenu = menu as TMenuType;
            this.IsMenuOpen = true;

            if (this.HasInventory)
                InitInventory();
        }

        /// <summary>Notifies the handler that it's native menu was closed.</summary>
        public virtual void Close()
        {
            this.IsMenuOpen = false;
            this.SplitMenu = null;
        }

        /// <summary>Runs on tick for handling things like highlighting text.</summary>
        public virtual void Update()
        {
            if (Game1.mouseClickPolling < GetRightClickPollingInterval())
            {
                this.SplitMenu?.Update();
            }
            else if (this.SplitMenu != null)
            {
                // Close the menu if the interval is reached as the player likely wants it's regular behavior
                CancelMove();
            }
        }

        /// <summary>Tells the handler to close the split menu.</summary>
        public virtual void CloseSplitMenu()
        {
            this.SplitMenu?.Close();
            this.SplitMenu = null;
            Helper.Events.Input.ButtonPressed -= OnButtonPressed;
        }

        /// <summary>Draws the split menu.</summary>
        public virtual void Draw(SpriteBatch spriteBatch)
        {
            if (this.SplitMenu != null)
            {
                this.SplitMenu.draw(spriteBatch);
            }
        }

        /// <summary>处理菜单打开</summary>
        public void HandleSplitMenu()
        {
            // Invoke split menu if the modifier key was also down
            if (CanOpenSplitMenu())
            {
                // Cancel the current operation
                if (this.SplitMenu != null)
                {
                    // TODO: return this value if it's consumed?
                    CancelMove();
                }

                // Store where the player clicked to pass to the native code after the split menu has been submitted so it remains the same even if the mouse moved.
                this.ClickItemLocation = new Point(Game1.getOldMouseX(), Game1.getOldMouseY());
                this.Monitor.DebugLog($"ClickItemLocation {LogExtensions.ToJson(this.ClickItemLocation)} {LogExtensions.ToJson(this.Helper.Input.GetCursorPosition())}");
                // Notify the handler the inventory was clicked.
                if (this.HasInventory && !this.Inventory.Initialized)
                    this.Monitor.Log("Handler has inventory but inventory isn't initialized.", LogLevel.Trace);
                // 背包初始化完毕且点击的是背包
                if (this.HasInventory && this.Inventory.Initialized && this.Inventory.WasClicked(Game1.getMouseX(), Game1.getMouseY()))
                {
                    this.HandleStatus = InventoryClicked();
                    Helper.Events.Input.ButtonPressed += OnButtonPressed;
                    return;
                }
                this.HandleStatus = OpenSplitMenu();
                Helper.Events.Input.ButtonPressed += OnButtonPressed;
                return;
            }
            this.HandleStatus = EInputHandled.NotHandled;
        }

        /// <summary>Raised after the player presses a button on the keyboard, controller, or mouse.</summary>
        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            // Forward input to the handler and consumes it while the tooltip is active.
            // Intercept keyboard input while the tooltip is active so numbers don't change the actively equipped item etc.
            // TODO: remove null checks if these events are only called subscribed when it's valid
            if (!StackSplitX.ToggleKey.Exists(item => item.JustPressed()))
            {
                this.HandleInput(e.Button);
                this.Monitor.Log($"{e.Button} {this.HandleStatus}");
                switch (this.HandleStatus)
                {
                    // 处理中
                    case EInputHandled.Handled:
                        // Obey unless we're hitting 'cancel' keys.
                        if (e.Button != SButton.Escape)
                            this.Helper.Input.Suppress(e.Button);
                        else
                            this.CloseSplitMenu();
                        break;
                    // 处理结束
                    case EInputHandled.Consumed:
                        this.Helper.Input.Suppress(e.Button);
                        break;
                    // 不需要处理
                    case EInputHandled.NotHandled:
                        if (e.Button == SButton.MouseLeft || e.Button == SButton.MouseRight)
                            this.CloseSplitMenu(); // click wasn't handled meaning the split menu no longer has focus and should be closed.
                        break;
                }
            }
        }


        /// <summary>Handle user input.</summary>
        /// <param name="button">The pressed button.</param>
        public void HandleInput(SButton button)
        {
            // Was right click pressed
            if (button == SButton.MouseRight)
            {
                this.HandleStatus = EInputHandled.NotHandled;
                return;
            }
            else if (button == SButton.MouseLeft)
            {
                // If the player clicks within the bounds of the tooltip then forward the input to that. 
                // Otherwise they're clicking elsewhere and we should close the tooltip.
                if (this.SplitMenu != null && this.SplitMenu.ContainsPoint(Game1.getMouseX(), Game1.getMouseY()))
                {
                    this.SplitMenu.receiveLeftClick(Game1.getMouseX(), Game1.getMouseY());
                    this.HandleStatus = EInputHandled.Consumed;
                    return;
                }

                var handled = HandleLeftClick();
                if (handled == EInputHandled.NotHandled && this.SplitMenu != null)
                {
                    // Lost focus; cancel the move (run default behavior) 失去焦点 关闭菜单 执行原始操作
                    this.HandleStatus = CancelMove();
                    return;
                }
                this.HandleStatus = handled;
                return;
            }
            else if (ShouldConsumeKeyboardInput(button))
            {
                this.HandleStatus = EInputHandled.Handled;
                return;
            }
            this.HandleStatus = EInputHandled.NotHandled;
        }


        /// <summary>Allows derived classes to handle left clicks when they are not focused on the split menu.</summary>
        /// <returns>If the input was handled or consumed.</returns>
        protected virtual EInputHandled HandleLeftClick()
        {
            return EInputHandled.NotHandled;
        }

        /// <summary>Whether we should consume the input, preventing it from reaching the game. 是否应该消耗输入，阻止传给游戏</summary>
        /// <param name="keyPressed">The key that was pressed.</param>
        /// <returns>True if it should be consumed, false otherwise.</returns>
        protected virtual bool ShouldConsumeKeyboardInput(SButton keyPressed)
        {
            return this.SplitMenu != null;
        }

        /// <summary>How long the right click has to be held for before the receiveRIghtClick gets called rapidly (See Game1.Update)</summary>
        /// <returns>The polling interval.</returns>
        protected virtual float GetRightClickPollingInterval()
        {
            return 650f;
        }

        /// <summary>Allows derived handlers to provide additional checks before opening the split menu.</summary>
        /// <returns>True if it can be opened.</returns>
        protected virtual bool CanOpenSplitMenu()
        {
            return true;
        }

        /// <summary>Main event that derived handlers use to setup necessary hooks and other things needed to take over how the stack is split.</summary>
        /// <returns>If the input was handled or consumed.</returns>
        protected abstract EInputHandled OpenSplitMenu();

        /// <summary>Alternative of OpenSplitMenu which is invoked when the generic inventory handler is clicked.</summary>
        /// <returns>If the input was handled or consumed.</returns>
        protected virtual EInputHandled InventoryClicked()
        {
            Debug.Assert(this.HasInventory);
            return OpenSplitMenu();
        }

        /// <summary>Called when the current handler loses focus when the split menu is open, allowing it to cancel the operation or run the default behaviour. 取消输入动作</summary>
        /// <returns>If the input was handled or consumed.</returns>
        protected virtual EInputHandled CancelMove()
        {
            CloseSplitMenu();

            if (this.HasInventory && this.Inventory.Initialized)
                this.Inventory.CancelSplit();

            return EInputHandled.NotHandled;
        }

        /// <summary>Callback given to the split menu that is invoked when a value is submitted.</summary>
        /// <param name="s">The user input.</param>
        protected virtual void OnStackAmountReceived(string s)
        {
            CloseSplitMenu();
        }

        /// <summary>Initializes the inventory using the most common variable names.</summary>
        protected virtual void InitInventory()
        {
            if (!this.HasInventory)
                return;

            try
            {
                var inventoryMenu = this.NativeMenu.GetType().GetField("inventory").GetValue(this.NativeMenu) as InventoryMenu;
                var hoveredItemField = Helper.Reflection.GetField<Item>(this.NativeMenu, "hoveredItem");

                this.Inventory.Init(inventoryMenu, hoveredItemField);
            }
            catch (Exception e)
            {
                this.Monitor.Log($"Failed to initialize the inventory handler: {e}", LogLevel.Error);
            }
        }

        /// <summary>判断是否按下左Alt或左Shift</summary>
        protected bool IsModifierKeyDown()
        {
            return
                this.Helper.Input.IsDown(SButton.LeftAlt)
                || this.Helper.Input.IsDown(SButton.LeftShift);
        }
    }
}
