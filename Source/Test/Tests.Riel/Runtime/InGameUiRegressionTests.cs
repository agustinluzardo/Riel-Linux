using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;

using Riel.Common;
using Riel.Common.Input;
using Riel.Common.Position;
using Riel.Graphics.Window;
using Riel.Graphics.Window.Controls;
using Riel.Graphics.Xna;
using Riel.Models.Settings;

using GetText;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

using Orts.ActivityRunner.Viewer3D;
using Orts.Simulation;

namespace Tests.Riel.Runtime
{
    [TestClass]
    [DoNotParallelize]
    public class InGameUiRegressionTests
    {
        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void LeavingTextInputRestoresGameCommands(bool closeWindow)
        {
            UserCommandController<UserCommand> commands = new UserCommandController<UserCommand>();
            UserCommandController<UserCommand> top = commands.AddTopLayerController();
            int executed = 0;
            commands.AddEvent(UserCommand.GamePause, KeyEventType.KeyPressed, () => executed++);
            WindowManager manager = CreateManager(top);
            WindowBase window = CreateWindow(manager);
            Assert.IsTrue(window.Open());
            TextInput input = CreateTextInput(window);
            SetProperty(window, "ActiveControl", input);

            Assert.IsTrue(top.SuppressDownLevelEventHandling, "Text focus must suppress commands before the first character.");
            Assert.IsTrue((bool)Call(input, "HandleTextInput", new TextInputEventArgs('\b', Keys.Back)));
            TriggerPause(commands);
            Assert.AreEqual(0, executed);

            if (closeWindow)
                Assert.IsTrue(window.Close());
            else
            {
                PointerCommandArgs pointer = new PointerCommandArgs();
                SetProperty(pointer, "Position", new Point(-1, -1));
                Call(manager, "MousePressedEvent", pointer, KeyModifiers.None);
            }

            Assert.IsFalse(top.SuppressDownLevelEventHandling);
            TriggerPause(commands);
            Assert.AreEqual(1, executed, "Driving commands must resume after closing text input or clicking away.");
        }

        [TestMethod]
        [DataRow(false, '\r')]
        [DataRow(false, '\u001b')]
        [DataRow(true, '\r')]
        [DataRow(true, '\u001b')]
        public void EnterAndEscapeReleaseTextFocusWithoutReleasingAnOpenModal(bool modal, char character)
        {
            UserCommandController<UserCommand> controller = new UserCommandController<UserCommand>();
            WindowManager manager = CreateManager(controller);
            WindowBase window = CreateWindow(manager, modal);
            Assert.IsTrue(window.Open());
            TextInput input = CreateTextInput(window);
            SetProperty(window, "ActiveControl", input);
            int entered = 0;
            int escaped = 0;
            input.OnEnterKey += (_, _) => entered++;
            input.OnEscapeKey += (_, _) => escaped++;

            Assert.IsTrue((bool)Call(input, "HandleTextInput", new TextInputEventArgs(character, character == '\r' ? Keys.Enter : Keys.Escape)));

            Assert.IsNull(GetProperty(window, "ActiveControl"));
            Assert.AreEqual(modal, controller.SuppressDownLevelEventHandling);
            Assert.AreEqual(character == '\r' ? 1 : 0, entered);
            Assert.AreEqual(character == '\u001b' ? 1 : 0, escaped);
            Assert.IsTrue(window.Close());
            Assert.IsFalse(controller.SuppressDownLevelEventHandling);
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void ReleasingTextFocusPreservesModalSuppression(bool modal)
        {
            UserCommandController<UserCommand> controller = new UserCommandController<UserCommand>();
            WindowManager manager = CreateManager(controller);
            WindowBase window = CreateWindow(manager, modal);
            Assert.IsTrue(window.Open());
            TextInput input = CreateTextInput(window);
            SetProperty(window, "ActiveControl", input);
            int entered = 0;
            input.OnEnterKey += (_, _) => entered++;

            input.ReleaseFocus();

            Assert.AreEqual(1, entered);
            Assert.AreEqual(modal, controller.SuppressDownLevelEventHandling);
            Assert.IsTrue(window.Close());
            Assert.IsFalse(controller.SuppressDownLevelEventHandling);
        }

        [TestMethod]
        public void ClosingNestedModalKeepsCommandsSuppressedUntilTheLastModalCloses()
        {
            UserCommandController<UserCommand> controller = new UserCommandController<UserCommand>();
            WindowManager manager = CreateManager(controller);
            manager.MultiLayerModalWindows = true;
            WindowBase outer = CreateWindow(manager, true);
            WindowBase inner = CreateWindow(manager, true);
            Assert.IsTrue(outer.Open());
            SetProperty(outer, "ActiveControl", CreateTextInput(outer));
            Assert.IsTrue(inner.Open());
            Assert.IsTrue(inner.Close());
            Assert.IsTrue(controller.SuppressDownLevelEventHandling);
            Assert.IsNotNull(GetProperty(outer, "ActiveControl"), "The previous modal restores its text focus.");
            Assert.IsTrue(outer.Close());
            Assert.IsFalse(controller.SuppressDownLevelEventHandling);
        }

        [TestMethod]
        public void ModalCloseNotificationObservesReleasedGameCommands()
        {
            UserCommandController<UserCommand> controller = new UserCommandController<UserCommand>();
            WindowManager manager = CreateManager(controller);
            WindowBase window = CreateWindow(manager, true);
            bool notified = false;
            manager.OnModalWindow += (_, args) =>
            {
                if (!args.ModalWindowOpen)
                {
                    Assert.IsFalse(controller.SuppressDownLevelEventHandling);
                    notified = true;
                }
            };
            Assert.IsTrue(window.Open());
            Assert.IsTrue(window.Close());
            Assert.IsTrue(notified);
        }

        [TestMethod]
        public void TextFocusCanBePreparedBeforeACommandControllerIsBound()
        {
            WindowManager manager = CreateManager(null);
            WindowBase window = CreateWindow(manager);
            Assert.IsTrue(window.Open());
            SetProperty(window, "ActiveControl", CreateTextInput(window));
            UserCommandController<UserCommand> controller = new UserCommandController<UserCommand>();
            SetField(manager, "<UserCommandController>k__BackingField", controller);

            Call(manager, "UpdateInputSuppression");

            Assert.IsTrue(controller.SuppressDownLevelEventHandling);
            Assert.IsTrue(window.Close());
            Assert.IsFalse(controller.SuppressDownLevelEventHandling);
        }

        [TestMethod]
        public void LocationModeChangesRefreshLabelsWithoutMovingTheCamera()
        {
            WithSimulator(() =>
            {
                FormBase overlay = CreateLocationOverlay(out List<WorldFile> worldFiles, out Collection<WindowControl> controls);
                WorldLocation camera = new WorldLocation(0, 0, 0, 0, 0);
                Assert.IsTrue(overlay.Open());
                Call(overlay, "UpdateLocationLabels", camera, worldFiles);
                Assert.AreEqual(2, controls.Count, "Platforms mode includes two platforms.");

                ModifiableKeyCommandArgs args = new ModifiableKeyCommandArgs();
                SetProperty(args, "AdditionalModifiers", KeyModifiers.Shift);
                foreach (int expected in new[] { 1, 1, 3 })
                {
                    Call(overlay, "TabAction", args);
                    Call(overlay, "UpdateLocationLabels", camera, worldFiles);
                    Assert.AreEqual(expected, controls.Count, "Sidings, Auto, and All must apply at the same tile.");
                }

                // An unchanged update must keep the cached layout; the extra
                // marker is removed only when a real invalidation rebuilds it.
                controls.Add(null);
                Call(overlay, "UpdateLocationLabels", camera, worldFiles);
                Assert.AreEqual(4, controls.Count, "Unchanged frames must not rescan the scenery.");
            });
        }

        [TestMethod]
        public void ReopeningLocationLabelsRefreshesTheSameTile()
        {
            WithSimulator(() =>
            {
                FormBase overlay = CreateLocationOverlay(out List<WorldFile> worldFiles, out Collection<WindowControl> controls);
                WorldLocation camera = new WorldLocation(0, 0, 0, 0, 0);
                Assert.IsTrue(overlay.Open());
                Call(overlay, "UpdateLocationLabels", camera, worldFiles);
                controls.Add(null);
                Assert.IsTrue(overlay.Close());
                Assert.IsTrue(overlay.Open());
                Call(overlay, "UpdateLocationLabels", camera, worldFiles);
                Assert.AreEqual(2, controls.Count);
            });
        }

        [TestMethod]
        public void NewlyLoadedSceneryRefreshesLabelsWhileTheCameraStaysInTheSameTile()
        {
            FormBase overlay = CreateLocationOverlay(out List<WorldFile> worldFiles, out Collection<WindowControl> controls);
            WorldLocation camera = new WorldLocation(0, 0, 0, 0, 0);
            Call(overlay, "UpdateLocationLabels", camera, worldFiles);
            Assert.AreEqual(2, controls.Count);
            Call(overlay, "UpdateLocationLabels", camera, new List<WorldFile>());
            Assert.AreEqual(0, controls.Count);
        }

        private static FormBase CreateLocationOverlay(out List<WorldFile> worldFiles, out Collection<WindowControl> controls)
        {
            Type type = typeof(Viewer).Assembly.GetType("Orts.ActivityRunner.Viewer3D.PopupWindows.LocationOverlay", true);
            FormBase overlay = (FormBase)RuntimeHelpers.GetUninitializedObject(type);
            WindowManager manager = CreateManager(new UserCommandController<UserCommand>());
            SetField(overlay, "<Owner>k__BackingField", manager);
            SetField(overlay, "<Catalog>k__BackingField", CatalogManager.Catalog);
            SetField(overlay, "userCommandController", manager.UserCommandController);
            SetField(overlay, "userSettings", new ProfileUserSettingsModel());
            SetField(overlay, "labelList", new List<Label3DOverlay>());
            SetField(overlay, "labelsDirty", true);
            SetField(overlay, "autoPlatforms", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Platform A" });
            SetField(overlay, "autoSidings", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            object layout = RuntimeHelpers.GetUninitializedObject(typeof(WindowBase).Assembly.GetType("Riel.Graphics.Window.Controls.Layout.WindowControlLayout", true));
            controls = new Collection<WindowControl>();
            SetField(layout, "<Controls>k__BackingField", controls);
            SetField(overlay, "controlLayout", layout);

            WorldFile worldFile = (WorldFile)RuntimeHelpers.GetUninitializedObject(typeof(WorldFile));
            TrItemLabel platformA = CreateTrackLabel("Platform A");
            TrItemLabel platformB = CreateTrackLabel("Platform B");
            TrItemLabel siding = CreateTrackLabel("Siding");
            SetField(worldFile, "<Platforms>k__BackingField", new List<TrItemLabel> { platformA, platformB });
            SetField(worldFile, "<Sidings>k__BackingField", new List<TrItemLabel> { siding });
            worldFiles = new List<WorldFile> { worldFile };

            // Seed the real resource cache so filtering exercises production code
            // without allocating font textures or requiring a graphics device.
            ResourceGameComponent<Label3DOverlay, int> cache = (ResourceGameComponent<Label3DOverlay, int>)RuntimeHelpers.GetUninitializedObject(typeof(ResourceGameComponent<Label3DOverlay, int>));
            ConcurrentDictionary<int, Label3DOverlay> resources = new ConcurrentDictionary<int, Label3DOverlay>();
            foreach (TrItemLabel label in new[] { platformA, platformB, siding })
                resources[label.GetHashCode()] = null;
            SetField(cache, "currentResources", resources);
            SetField(overlay, "labelCache", cache);
            return overlay;
        }

        private static TrItemLabel CreateTrackLabel(string name)
        {
            TrItemLabel label = (TrItemLabel)RuntimeHelpers.GetUninitializedObject(typeof(TrItemLabel));
            SetField(label, "ItemName", name);
            return label;
        }

        private static void WithSimulator(Action action)
        {
            Simulator previous = Simulator.Instance;
            Simulator simulator = (Simulator)RuntimeHelpers.GetUninitializedObject(typeof(Simulator));
            SetField(simulator, "<Confirmer>k__BackingField", RuntimeHelpers.GetUninitializedObject(typeof(Confirmer)));
            typeof(Simulator).GetProperty(nameof(Simulator.Instance)).SetValue(null, simulator);
            try { action(); }
            finally { typeof(Simulator).GetProperty(nameof(Simulator.Instance)).SetValue(null, previous); }
        }

        private static WindowManager CreateManager(UserCommandController<UserCommand> controller)
        {
            WindowManager manager = (WindowManager)RuntimeHelpers.GetUninitializedObject(typeof(WindowManager));
            GC.SuppressFinalize(manager);
            SetField(manager, "windows", new List<FormBase>());
            SetField(manager, "modalWindows", new Stack<WindowBase>());
            SetField(manager, "size", new Point(800, 600));
            SetField(manager, "<UserCommandController>k__BackingField", controller);
            Type comparer = typeof(WindowManager).GetNestedType("WindowSortComparer", BindingFlags.NonPublic);
            SetField(manager, "windowSortComparer", Activator.CreateInstance(comparer, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { manager }, null));
            return manager;
        }

        private static WindowBase CreateWindow(WindowManager manager, bool modal = false)
        {
            WindowBase window = (WindowBase)RuntimeHelpers.GetUninitializedObject(typeof(TestWindow));
            SetField(window, "<Owner>k__BackingField", manager);
            SetField(window, "<Modal>k__BackingField", modal);
            SetField(window, "borderRect", new Rectangle(0, 0, 400, 200));
            SetField(window, "location", new Point(0, 0));
            return window;
        }

        private static TextInput CreateTextInput(WindowBase window)
        {
            TextInput input = (TextInput)RuntimeHelpers.GetUninitializedObject(typeof(TextInput));
            SetField(input, "<Window>k__BackingField", window);
            return input;
        }

        private static void TriggerPause(UserCommandController<UserCommand> controller) =>
            typeof(UserCommandController<UserCommand>).GetMethod("Trigger", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(UserCommand), typeof(KeyEventType), typeof(UserCommandArgs), typeof(GameTime) }, null)
                .Invoke(controller, new object[] { UserCommand.GamePause, KeyEventType.KeyPressed, new UserCommandArgs(), new GameTime() });

        private sealed class TestWindow : WindowBase
        {
            private TestWindow() : base(null, null, default, default, null) { }
        }

        private static object Call(object target, string name, params object[] args) =>
            Method(target.GetType(), name).Invoke(target, args);

        private static MethodInfo Method(Type type, string name) =>
            type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private static object GetProperty(object target, string name) =>
            typeof(WindowBase).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target);

        private static void SetProperty(object target, string name, object value)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (property != null)
                {
                    property.SetValue(target, value);
                    return;
                }
            }
            throw new MissingMemberException(target.GetType().FullName, name);
        }

        private static void SetField(object target, string name, object value)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }
            throw new MissingFieldException(target.GetType().FullName, name);
        }
    }
}
