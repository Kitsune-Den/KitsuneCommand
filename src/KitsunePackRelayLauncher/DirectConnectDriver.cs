using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace KitsunePackRelayLauncher
{
    /// <summary>
    /// Joins a server the way a player would: opens 7DTD's Direct
    /// Connect dialog, types the host and port in, and presses
    /// Connect. The game's own handler then does the rest (hostname
    /// resolution, the multiplayer-privilege check, the connect
    /// itself), so we never call into the connection code directly.
    ///
    /// Everything on the dialog is bound by name at runtime. The
    /// compile-time refs are V2.5, where Direct Connect was a panel
    /// inside the server browser (<c>Show</c>/<c>Hide</c>, button
    /// <c>btnDirectConnectConnect</c>). 3.x made it its own window
    /// with a static <c>Open(XUi, Action, Action)</c> and renamed the
    /// button to <c>btnConnect</c>. Referencing either shape directly
    /// would throw MissingMethodException on the other.
    ///
    /// Failure is always soft. If the game doesn't have the 3.x
    /// dialog, we log where to connect and leave the menu alone. If
    /// the dialog opens but Connect never enables, we leave it open
    /// with the address filled in, so the player is one click away.
    /// </summary>
    internal static class DirectConnectDriver
    {
        private const string Tag = "[KitsunePackRelayLauncher] ";

        /// <summary>
        /// Let the main menu finish opening first. OnOpen opens the
        /// global menu windows and may queue its own dialogs; opening
        /// ours from inside that call races them.
        /// </summary>
        private const float SettleSeconds = 0.5f;

        /// <summary>
        /// Connect's enabled state follows the dialog's validdata
        /// binding, which refreshes on the dialog's next dirty
        /// update. A frame or two is normal; give up after ~1s.
        /// </summary>
        private const int EnableWaitFrames = 60;

        private const BindingFlags AnyInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyStatic =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static void Start(XUiController mainMenu, string host, int port)
        {
            ThreadManager.StartCoroutine(Run(mainMenu, host, port));
        }

        private static IEnumerator Run(XUiController mainMenu, string host, int port)
        {
            yield return new WaitForSecondsRealtime(SettleSeconds);

            XUiController dialog = OpenAndFill(mainMenu, host, port);
            if (dialog == null) yield break;

            XUiController connect = FindConnectButton(dialog);
            if (connect == null)
            {
                Log.Warning(Tag + "Direct Connect has no btnConnect / btnDirectConnectConnect on " +
                            "this game version. Left the dialog open with " + host + ":" + port +
                            " filled in; press Connect to join.");
                yield break;
            }

            for (int i = 0; i < EnableWaitFrames && !IsEnabled(connect); i++)
            {
                yield return null;
            }

            if (!IsEnabled(connect))
            {
                Log.Warning(Tag + "Connect stayed disabled for " + host + ":" + port +
                            " (the dialog didn't accept the address). Left it open " +
                            "so you can check it and connect by hand.");
                yield break;
            }

            PressConnect(dialog, host, port);
        }

        private static XUiController OpenAndFill(XUiController mainMenu, string host, int port)
        {
            try
            {
                XUi xui = GetXui(mainMenu);
                if (xui == null)
                {
                    Log.Warning(Tag + "Couldn't reach the main menu's XUi; not auto-connecting to " +
                                host + ":" + port + ".");
                    return null;
                }

                Type dialogType = typeof(XUiC_ServerBrowserDirectConnect);
                MethodInfo open = dialogType.GetMethod(
                    "Open", AnyStatic, null,
                    new[] { typeof(XUi), typeof(Action), typeof(Action) }, null);
                if (open == null)
                {
                    Log.Warning(Tag + "This game version has no standalone Direct Connect " +
                                "dialog (it arrived in 7DTD 3.0), so auto-connect is off. " +
                                "Join " + host + ":" + port + " from Join a Game > Connect to IP.");
                    return null;
                }

                // Null callbacks: there's no server browser behind us
                // to close or return focus to. Cancel just drops the
                // player back on the main menu.
                open.Invoke(null, new object[] { xui, null, null });

                XUiC_ServerBrowserDirectConnect dialog =
                    xui.GetChildByType<XUiC_ServerBrowserDirectConnect>();
                if (dialog == null)
                {
                    Log.Warning(Tag + "Opened Direct Connect but couldn't find it in the XUi tree; " +
                                "not auto-connecting to " + host + ":" + port + ".");
                    return null;
                }

                // The dialog's OnOpen just loaded the last-used address
                // from GamePrefs. Overwrite it with the launcher's.
                XUiC_TextInput txtIp = GetField<XUiC_TextInput>(dialog, "txtIp");
                XUiC_TextInput txtPort = GetField<XUiC_TextInput>(dialog, "txtPort");
                if (txtIp == null || txtPort == null)
                {
                    Log.Warning(Tag + "Direct Connect has no txtIp / txtPort on this game version; " +
                                "join " + host + ":" + port + " by hand.");
                    return null;
                }

                txtIp.Text = host;
                txtPort.Text = port.ToString();

                // Setting Text from code doesn't necessarily raise the
                // change event that recomputes validdata, so run the
                // validator ourselves, the way OnOpen does.
                MethodInfo validate = dialogType.GetMethod(
                    "validateIpPort", AnyInstance, null,
                    new[] { typeof(XUiController), typeof(string), typeof(bool) }, null);
                validate?.Invoke(dialog, new object[] { null, null, true });
                dialog.IsDirty = true;

                Log.Out(Tag + "Direct Connect opened with " + host + ":" + port + ".");
                return dialog;
            }
            catch (Exception ex)
            {
                Log.Warning(Tag + "Couldn't open Direct Connect for " + host + ":" + port + ": " +
                            Unwrap(ex).Message);
                return null;
            }
        }

        private static void PressConnect(XUiController dialog, string host, int port)
        {
            try
            {
                MethodInfo press = typeof(XUiC_ServerBrowserDirectConnect).GetMethod(
                    "BtnConnect_OnPressed", AnyInstance, null,
                    new[] { typeof(XUiController), typeof(int) }, null);
                if (press == null)
                {
                    Log.Warning(Tag + "Direct Connect has no BtnConnect_OnPressed on this game " +
                                "version. Left the dialog open with " + host + ":" + port +
                                " filled in; press Connect to join.");
                    return;
                }

                Log.Out(Tag + "Auto-connecting to " + host + ":" + port + ".");
                // -1 is the mouse button the game itself passes when
                // Connect is pressed from the keyboard or a text field.
                press.Invoke(dialog, new object[] { dialog, -1 });
            }
            catch (Exception ex)
            {
                Log.Warning(Tag + "Pressing Connect for " + host + ":" + port + " threw: " +
                            Unwrap(ex).Message);
            }
        }

        private static XUiController FindConnectButton(XUiController dialog)
        {
            return GetField<XUiController>(dialog, "btnConnect")
                   ?? GetField<XUiController>(dialog, "btnDirectConnectConnect");
        }

        private static bool IsEnabled(XUiController button)
        {
            try
            {
                return button.ViewComponent != null && button.ViewComponent.Enabled;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// <c>XUiController.xui</c> is a property in V2.5 and a plain
        /// field in 3.x, so <c>controller.xui</c> compiled against the
        /// V2.5 refs calls a getter 3.x doesn't have.
        /// </summary>
        private static XUi GetXui(XUiController controller)
        {
            Type type = typeof(XUiController);
            FieldInfo field = type.GetField("xui", AnyInstance);
            if (field != null) return field.GetValue(controller) as XUi;
            PropertyInfo prop = type.GetProperty("xui", AnyInstance);
            return prop?.GetValue(controller, null) as XUi;
        }

        private static T GetField<T>(object owner, string name) where T : class
        {
            FieldInfo field = owner.GetType().GetField(name, AnyInstance);
            return field?.GetValue(owner) as T;
        }

        private static Exception Unwrap(Exception ex)
        {
            return ex is TargetInvocationException && ex.InnerException != null
                ? ex.InnerException
                : ex;
        }
    }
}
