using System;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;
using Autodesk.Revit.UI;

namespace TNovCommon.Help
{
    /// <summary>
    /// Статический фасад панели справки. Владеет единственным экземпляром контрола,
    /// маршалит смену раздела на UI-поток Revit и откладывает показ панели,
    /// если её нельзя показать прямо сейчас.
    ///
    /// Показ панели поверх модального диалога плагина бесполезен: диалоги
    /// привязываются к главному окну Revit через WindowInteropHelper.Owner и
    /// гасят ввод у дочерних окон фрейма, включая закреплённые панели. Поэтому
    /// раздел переключается всегда, а Show() уходит в очередь и выполняется
    /// из OnIdling — там гарантированно нет активного модального диалога.
    /// </summary>
    public static class HelpPaneHost
    {
        private static Dispatcher _uiDispatcher;
        private static HelpPaneControl _control;
        private static string _pendingSection;
        private static int _sectionCallbackQueued;
        private static int _showPending;

        /// <summary>Вызывается из Application.OnStartup — там мы на UI-потоке Revit.</summary>
        public static void Initialize(Dispatcher uiDispatcher)
        {
            _uiDispatcher = uiDispatcher;
        }

        /// <summary>
        /// Единственный экземпляр контрола. Создаётся лениво, на UI-потоке:
        /// SetupDockablePane может вызываться Revit'ом несколько раз
        /// (dock/float, восстановление раскладки) и обязан возвращать один и тот же элемент.
        /// </summary>
        internal static HelpPaneControl GetOrCreateControl()
        {
            if (_control == null)
            {
                _control = new HelpPaneControl();

                string section = Volatile.Read(ref _pendingSection);
                if (!string.IsNullOrEmpty(section))
                    _control.ShowSection(section);
            }

            return _control;
        }

        /// <summary>
        /// Переключить раздел. Безопасно вызывать из любого потока: команды с
        /// прогрессбаром живут на собственном потоке со своим Dispatcher, поэтому
        /// маршалим на захваченный UI-диспетчер и только через BeginInvoke —
        /// Invoke с рабочего потока при занятом главном даёт взаимную блокировку.
        /// </summary>
        public static void SetSection(string sectionKey)
        {
            if (string.IsNullOrEmpty(sectionKey))
                return;

            Volatile.Write(ref _pendingSection, sectionKey);

            Dispatcher dispatcher = _uiDispatcher;
            if (dispatcher == null)
                return;

            if (dispatcher.CheckAccess())
            {
                ApplySection();
                return;
            }

            // Склейка: в очереди держим не более одного колбэка, применяем последний ключ.
            if (Interlocked.Exchange(ref _sectionCallbackQueued, 1) == 0)
                dispatcher.BeginInvoke(new Action(ApplySection), DispatcherPriority.Background);
        }

        private static void ApplySection()
        {
            Interlocked.Exchange(ref _sectionCallbackQueued, 0);

            try
            {
                HelpPaneControl control = _control;
                if (control == null)
                    return; // панель ещё не создавали — ключ подхватится в GetOrCreateControl

                string section = Volatile.Read(ref _pendingSection);
                if (!string.IsNullOrEmpty(section))
                    control.ShowSection(section);
            }
            catch (Exception)
            {
                // Справка не должна ронять команду.
            }
        }

        /// <summary>
        /// Взять справку по функции на себя. Возвращает false, если статьи нет в бандле
        /// или панель не зарегистрирована в этой сессии — тогда вызывающий уходит в браузер.
        /// </summary>
        public static bool TryShow(string sectionKey)
        {
            try
            {
                if (!HelpBundle.HasTopicFor(sectionKey))
                    return false;
                if (!DockablePane.PaneIsRegistered(HelpPaneIds.Help))
                    return false;
            }
            catch (Exception)
            {
                return false;
            }

            RequestShow(sectionKey);
            return true;
        }
        /// <summary>Открыть панель на нужном разделе (или отложить показ до Idling).</summary>
        public static void RequestShow(string sectionKey)
        {
            SetSection(sectionKey);
            Interlocked.Exchange(ref _showPending, 1);

            Dispatcher dispatcher = _uiDispatcher;
            if (dispatcher == null || !dispatcher.CheckAccess())
                return;

            // Модальный диалог плагина: панель появится, но будет неактивна. Ждём Idling.
            if (ComponentDispatcher.IsThreadModal)
                return;

            Show(RevitAPI.UiApplication);
        }

        /// <summary>
        /// Досылает отложенный показ. Вызывается из Application.OnIdling —
        /// это единственный контекст, где показ гарантированно осмыслен.
        /// </summary>
        public static void DrainPendingShow(UIApplication uiApp)
        {
            if (Volatile.Read(ref _showPending) == 0)
                return;

            Show(uiApp);
        }

        private static void Show(UIApplication uiApp)
        {
            if (uiApp == null)
                return;

            try
            {
                // Зарегистрирована != создана: GetDockablePane бросает исключение и во втором случае.
                if (!DockablePane.PaneIsRegistered(HelpPaneIds.Help))
                    return;
                if (!DockablePane.PaneExists(HelpPaneIds.Help))
                    return;

                // DockablePane — disposable-обёртка, не кэшируем её.
                DockablePane pane = uiApp.GetDockablePane(HelpPaneIds.Help);
                pane.Show();

                Interlocked.Exchange(ref _showPending, 0);
            }
            catch (Exception)
            {
                // Оставляем флаг: попробуем на следующем Idling.
            }
        }

        /// <summary>Вызывается из Application.OnShutdown. Снять регистрацию панели API не позволяет.</summary>
        public static void Shutdown()
        {
            HelpBundle.Dispose();
            _control = null;
            _uiDispatcher = null;
            Volatile.Write(ref _pendingSection, null);
            Interlocked.Exchange(ref _showPending, 0);
            Interlocked.Exchange(ref _sectionCallbackQueued, 0);
        }
    }
}
