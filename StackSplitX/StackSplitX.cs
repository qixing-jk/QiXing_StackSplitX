using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Threading;
using StackSplitX.MenuHandlers;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;

namespace StackSplitX
{
    public class StackSplitX : Mod
    {
        /// <summary>Mod配置项</summary>
        private ModConfig Config;

        /// <summary>快捷键绑定列表</summary>
        public static List<KeybindList> ToggleKey = new List<KeybindList>();

        /// <summary>Are we subscribed to the events listened to while a handler is active.</summary>
        private bool IsSubscribed = false;

        /// <summary>Handlers mapped to the type of menu they handle. 存储菜单类型和处理程序之间的对应关系</summary>
        private Dictionary<Type, IMenuHandler> MenuHandlers;

        /// <summary>The handler for the current menu.</summary>
        private IMenuHandler CurrentMenuHandler;

        /// <summary>Used to avoid resize events sent to menu changed. 是否为调整大小事件</summary>
        private bool WasResizeEvent = false;

        /// <summary>An index incremented on every tick and reset every 60th tick (0–59).</summary>
        private int CurrentUpdateTick = 0;

        /// <summary>Tracks what tick a resize event occurs on so we can resize the current handler next frame. -1 means no resize event.</summary>
        private int TickResizedOn = -1;

        /// <summary>Mod entry point.</summary>
        /// <param name="helper">Mod helper.</param>
        public override void Entry(IModHelper helper)
        {
            /// 添加事件处理程序
            helper.Events.Display.MenuChanged += OnMenuChanged;
            helper.Events.Display.WindowResized += OnWindowResized;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
#if DEBUG
            helper.Events.Specialized.UnvalidatedUpdateTicked += OnUnvalidatedUpdateTicked;
#endif


            this.Config = this.Helper.ReadConfig<ModConfig>();
            String[] ToggleAuxiliaryKey = this.Config.ToggleAuxiliaryKey;
            foreach (string key in ToggleAuxiliaryKey)
            {
                StackSplitX.ToggleKey.Add(KeybindList.Parse($"{key} + MouseRight"));
            }

            this.MenuHandlers = new Dictionary<Type, IMenuHandler>()
            {
                { typeof(GameMenu), new GameMenuHandler(helper, this.Monitor) },
                { typeof(ShopMenu), new ShopMenuHandler(helper, this.Monitor) },
                { typeof(ItemGrabMenu), new ItemGrabMenuHandler(helper, this.Monitor) },
                { typeof(CraftingPage), new CraftingMenuHandler(helper, this.Monitor) },
                { typeof(JunimoNoteMenu), new JunimoNoteMenuHandler(helper, this.Monitor) }
            };
        }

        private void OnUnvalidatedUpdateTicked(object sender, UnvalidatedUpdateTickedEventArgs e)
        {
            if (e.IsMultipleOf(120))
            {
                this.Monitor.DebugLog($"{LogExtensions.ToJson(this.CurrentMenuHandler?.HandleStatus)}");
                this.Monitor.DebugLog($"getMouse {LogExtensions.ToJson(Game1.getMouseX())} {LogExtensions.ToJson(Game1.getMouseY())}");
                this.Monitor.DebugLog($"getOldMouse {LogExtensions.ToJson(Game1.getOldMouseX())} {LogExtensions.ToJson(Game1.getOldMouseY())}");
                this.Monitor.DebugLog($"{LogExtensions.ToJson(this.Helper.Input.GetCursorPosition())}");
            }
        }

        /// <summary>Subscribes to the events we care about when a handler is active.</summary>
        private void SubscribeEvents()
        {
            if (!this.IsSubscribed)
            {
                // 按下任何按钮（键盘/鼠标/控制器）
                //Helper.Events.Input.ButtonPressed += OnButtonPressed;
                Helper.Events.Input.ButtonsChanged += OnButtonsChanged;
                // 渲染事件，为了渲染菜单
                Helper.Events.Display.Rendered += OnRendered;

                this.IsSubscribed = true;
            }
        }

        /// <summary>Unsubscribes from events when the handler is no longer active.</summary>
        private void UnsubscribeEvents()
        {
            if (this.IsSubscribed)
            {
                //Helper.Events.Input.ButtonPressed -= OnButtonPressed;
                Helper.Events.Input.ButtonsChanged -= OnButtonsChanged;

                Helper.Events.Display.Rendered -= OnRendered;

                this.IsSubscribed = false;
            }
        }

        /// <summary>Raised after the game window is resized.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnWindowResized(object sender, WindowResizedEventArgs e)
        {
            // set flags to notify handler to resize next tick as the menu isn't always recreated
            this.WasResizeEvent = true;
            this.TickResizedOn = this.CurrentUpdateTick;
        }

        /// <summary>Raised after a game menu is opened, closed, or replaced. 菜单状态改变</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnMenuChanged(object sender, MenuChangedEventArgs e)
        {
            // menu closed
            if (e.NewMenu == null)
            {
                // close the current handler and unsubscribe from the events
                if (this.CurrentMenuHandler != null)
                {
                    this.Monitor.DebugLog($"[OnMenuClosed] Closing current menu handler: {this.CurrentMenuHandler}", LogLevel.Trace);
                    this.CurrentMenuHandler.Close();
                    this.CurrentMenuHandler = null;

                    UnsubscribeEvents();
                }
                return;
            }

            // ignore resize event
            if (e.OldMenu?.GetType() == e.NewMenu?.GetType() && this.WasResizeEvent)
            {
                this.WasResizeEvent = false;
                return;
            }
            this.WasResizeEvent = false; // Reset


            // switch the currently handler to the one for the new menu type
            this.Monitor.Log($"Menu changed from {e.OldMenu} to {e.NewMenu}");
            var newMenuType = e.NewMenu.GetType();
            // 判断新窗口是否为可处理的窗口类型
            if (this.MenuHandlers.ContainsKey(newMenuType))
            {
                // Close the current one of it's valid 关闭上一个菜单处理程序
                if (this.CurrentMenuHandler != null)
                {
                    this.CurrentMenuHandler.Close();
                }
                // 开启新的菜单处理程序
                this.Monitor.DebugLog($"{CurrentMenuHandler} start processing");
                this.CurrentMenuHandler = this.MenuHandlers[newMenuType];
                this.CurrentMenuHandler.Open(e.NewMenu);

                SubscribeEvents();
            }
        }

        private void OnButtonsChanged(object sender, ButtonsChangedEventArgs e)
        {
            if (StackSplitX.ToggleKey.Exists(item => item.JustPressed()))
            {
                this.Monitor.Log($"before{LogExtensions.ToJson(e.Pressed)} {LogExtensions.ToJson(e.Held)} {LogExtensions.ToJson(e.Released)} {this.CurrentMenuHandler?.HandleStatus}");
                this.CurrentMenuHandler?.HandleSplitMenu();
                this.Monitor.Log($"after {LogExtensions.ToJson(e.Pressed)} {LogExtensions.ToJson(e.Held)} {LogExtensions.ToJson(e.Released)} {this.CurrentMenuHandler?.HandleStatus}");
                // 阻止原有按键逻辑
                foreach (var item in e.Pressed)
                {
                    this.Helper.Input.Suppress(item);
                }
                foreach (var item in e.Held)
                {
                    this.Helper.Input.Suppress(item);
                }
            }
        }


        /// <summary>Raised after the game state is updated (≈60 times per second).</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            this.CurrentUpdateTick += 1;
            if (this.CurrentUpdateTick >= 60)
                this.CurrentUpdateTick = 0;

            // If TickResizedOn isn't -1 then there was a resize event, so do the resize next tick.
            // We need to do it this way rather than where we ignore resize in menu changed since not all menus are recreated on resize,
            // and during the actual resize event the new menu will not have been created yet so we need to wait.
            if (this.TickResizedOn > -1 && this.TickResizedOn != this.CurrentUpdateTick)
            {
                this.TickResizedOn = -1;
                this.CurrentMenuHandler?.Close();
                // Checking the menu type since actions like returning to title will cause a resize event (idk why the window is maximized)
                // and the activeClickableMenu will not be what it was before.
                if (this.CurrentMenuHandler?.IsCorrectMenuType(Game1.activeClickableMenu) == true)
                {
                    this.CurrentMenuHandler?.Open(Game1.activeClickableMenu);
                }
                else
                {
                    this.CurrentMenuHandler = null;
                }
            }

            this.CurrentMenuHandler?.Update();
        }

        /// <summary>Raised after the game draws to the sprite patch in a draw tick, just before the final sprite batch is rendered to the screen. Since the game may open/close the sprite batch multiple times in a draw tick, the sprite batch may not contain everything being drawn and some things may already be rendered to the screen. Content drawn to the sprite batch at this point will be drawn over all vanilla content (including menus, HUD, and cursor).</summary>
        private void OnRendered(object sender, RenderedEventArgs e)
        {
            // tell the current handler to draw the split menu if it's active
            this.CurrentMenuHandler?.Draw(Game1.spriteBatch);
        }
    }
}
