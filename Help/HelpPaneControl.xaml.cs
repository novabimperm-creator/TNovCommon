using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace TNovCommon.Help
{
    /// <summary>
    /// Содержимое панели справки. Page, а не UserControl — это рекомендованный
    /// документацией Revit вариант для DockablePane и корректно сжимается по ширине.
    /// Живёт весь процесс, создаётся один раз через HelpPaneHost.
    /// </summary>
    public partial class HelpPaneControl : Page
    {
        /// <summary>
        /// Сколько разобранных документов держим. Документ владеет своими Image,
        /// то есть и декодированными битмапами, поэтому кэш маленький.
        /// </summary>
        private const int DocumentCacheLimit = 3;

        private readonly HtmlFlowDocumentRenderer _renderer;
        private readonly Dictionary<string, FlowDocument> _documents =
            new Dictionary<string, FlowDocument>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _documentOrder = new List<string>();

        private HelpBundleTopic _current;
        private bool _suppressSelection;

        public HelpPaneControl()
        {
            InitializeComponent();

            _renderer = new HtmlFlowDocumentRenderer(HelpBundle.ReadAsset, Resources, 800);
            _renderer.LinkClicked += OnLinkClicked;

            BuildTopicsTree(null);

            if (HelpBundle.Topics.Count == 0)
            {
                ShowMessage("Бандл справки не подключён к этой сборке. "
                            + "Соберите его: tools\\Build-HelpBundle.ps1");
            }
            else
            {
                ShowMessage("Выберите раздел в списке выше.");
            }
        }

        /// <summary>Переключить панель на раздел по ключу функции. Только UI-поток Revit.</summary>
        public void ShowSection(string sectionKey)
        {
            HelpBundleTopic topic;
            if (!HelpBundle.TryGetTopicFor(sectionKey, out topic))
                return;

            ShowTopic(topic);
        }

        private void ShowTopic(HelpBundleTopic topic)
        {
            if (topic == null)
                return;

            _current = topic;
            SectionTitle.Text = topic.Title;
            SelectInTree(topic);
            ArticleViewer.Document = GetDocument(topic);
        }

        private FlowDocument GetDocument(HelpBundleTopic topic)
        {
            FlowDocument cached;
            if (_documents.TryGetValue(topic.Slug, out cached))
            {
                Touch(topic.Slug);
                return cached;
            }

            string xhtml = HelpBundle.ReadArticle(topic);
            if (string.IsNullOrEmpty(xhtml))
                return Message("Статья не найдена в бандле справки.");

            FlowDocument document = _renderer.Render(xhtml);

            _documents[topic.Slug] = document;
            Touch(topic.Slug);
            TrimCache();

            return document;
        }

        private void Touch(string slug)
        {
            _documentOrder.Remove(slug);
            _documentOrder.Add(slug);
        }

        private void TrimCache()
        {
            while (_documentOrder.Count > DocumentCacheLimit)
            {
                string oldest = _documentOrder[0];
                _documentOrder.RemoveAt(0);
                _documents.Remove(oldest);
            }
        }

        private void ShowMessage(string text)
        {
            ArticleViewer.Document = Message(text);
        }

        private FlowDocument Message(string text)
        {
            var document = new FlowDocument(new Paragraph(new Run(text)))
            {
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize = 12,
                PagePadding = new Thickness(0)
            };

            var brush = Resources["MutedBrush"] as System.Windows.Media.Brush;
            if (brush != null)
                document.Foreground = brush;

            return document;
        }

        // ------------------------------------------------------------------
        // Оглавление
        // ------------------------------------------------------------------
        private void BuildTopicsTree(string filter)
        {
            _suppressSelection = true;
            try
            {
                TopicsTree.Items.Clear();

                IEnumerable<HelpBundleTopic> topics = HelpBundle.Topics
                    .Where(t => Matches(t, filter));

                foreach (HelpBundleTopic topic in topics)
                {
                    TopicsTree.Items.Add(new TreeViewItem
                    {
                        Header = topic.Title,
                        Tag = topic,
                        IsSelected = _current != null
                                     && string.Equals(_current.Slug, topic.Slug, StringComparison.OrdinalIgnoreCase)
                    });
                }
            }
            finally
            {
                _suppressSelection = false;
            }
        }

        private static bool Matches(HelpBundleTopic topic, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            return topic.Title != null
                   && topic.Title.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private void SelectInTree(HelpBundleTopic topic)
        {
            _suppressSelection = true;
            try
            {
                foreach (object item in TopicsTree.Items)
                {
                    var node = item as TreeViewItem;
                    var nodeTopic = node == null ? null : node.Tag as HelpBundleTopic;
                    if (nodeTopic == null)
                        continue;

                    if (string.Equals(nodeTopic.Slug, topic.Slug, StringComparison.OrdinalIgnoreCase))
                    {
                        node.IsSelected = true;
                        node.BringIntoView();
                        return;
                    }
                }
            }
            finally
            {
                _suppressSelection = false;
            }
        }

        // ------------------------------------------------------------------
        // Обработчики
        // ------------------------------------------------------------------
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            BuildTopicsTree(SearchBox.Text);
        }

        private void TopicsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_suppressSelection)
                return;

            var node = e.NewValue as TreeViewItem;
            var topic = node == null ? null : node.Tag as HelpBundleTopic;
            if (topic == null)
                return;

            _current = topic;
            SectionTitle.Text = topic.Title;
            ArticleViewer.Document = GetDocument(topic);
        }

        private void OpenInWikiButton_Click(object sender, RoutedEventArgs e)
        {
            string url = _current == null
                ? HelpLinks.WikiBase
                : HelpLinks.WikiBase + "#" + _current.Slug;

            OpenExternal(url);
        }

        private void OnLinkClicked(object sender, string href)
        {
            if (string.IsNullOrEmpty(href))
                return;

            // Внутренняя ссылка на другую статью — переключаем раздел, не браузер.
            if (href.StartsWith("#", StringComparison.Ordinal))
            {
                HelpBundleTopic topic;
                if (HelpBundle.TryGetTopicBySlug(href.Substring(1), out topic))
                {
                    ShowTopic(topic);
                    return;
                }

                OpenExternal(HelpLinks.WikiBase + href);
                return;
            }

            OpenExternal(href);
        }

        private static void OpenExternal(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // Справка не должна ронять Revit, если браузер не открылся.
            }
        }
    }
}
