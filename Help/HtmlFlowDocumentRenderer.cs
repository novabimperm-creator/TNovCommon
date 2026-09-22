using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;

namespace TNovCommon.Help
{
    /// <summary>
    /// Нормализованный XHTML из бандла в FlowDocument. Без внешних пакетов:
    /// разметку готовит генератор бандла, здесь остаётся только обход XmlReader.
    ///
    /// Настройки XmlReader заданы явно, потому что дефолты у net48 и net10
    /// различаются, а DtdProcessing.Prohibit к тому же делает любую именованную
    /// HTML-сущность жёсткой ошибкой — за это отвечает нормализатор в генераторе.
    /// </summary>
    public sealed class HtmlFlowDocumentRenderer
    {
        private static readonly XmlReaderSettings ReaderSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = false,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            CheckCharacters = false
        };

        private readonly Func<string, byte[]> _assetLoader;
        private readonly int _imageMaxWidth;
        private readonly ResourceDictionary _theme;

        /// <param name="assetLoader">Путь картинки внутри бандла в байты.</param>
        /// <param name="theme">Откуда брать кисти темы (Styles.xaml панели).</param>
        /// <param name="imageMaxWidth">
        /// Потолок ширины картинки: что шире — декодируется в эту ширину,
        /// что уже — остаётся в натуральном размере.
        /// </param>
        public HtmlFlowDocumentRenderer(Func<string, byte[]> assetLoader, ResourceDictionary theme, int imageMaxWidth)
        {
            _assetLoader = assetLoader;
            _theme = theme;
            _imageMaxWidth = imageMaxWidth > 0 ? imageMaxWidth : 640;
        }

        /// <summary>Событие для внешних ссылок: обработчик решает, что с ними делать.</summary>
        public event EventHandler<string> LinkClicked;

        public FlowDocument Render(string xhtml)
        {
            var document = new FlowDocument
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                LineHeight = 17,
                PagePadding = new Thickness(0, 0, 6, 0),
                Foreground = ThemeBrush("PrimaryTextBrush", Brushes.Black)
            };

            if (string.IsNullOrWhiteSpace(xhtml))
                return document;

            try
            {
                using (var stringReader = new StringReader(xhtml))
                using (XmlReader reader = XmlReader.Create(stringReader, ReaderSettings))
                {
                    var blocks = new BlockBuilder(this);
                    ReadNodes(reader, blocks, new InlineFormat());
                    blocks.Flush();

                    foreach (Block block in blocks.Blocks)
                        document.Blocks.Add(block);
                }
            }
            catch (XmlException e)
            {
                document.Blocks.Clear();
                document.Blocks.Add(new Paragraph(new Run(
                    "Не удалось разобрать статью справки: " + e.Message)));
            }

            return document;
        }

        // ------------------------------------------------------------------
        // Обход
        // ------------------------------------------------------------------
        private void ReadNodes(XmlReader reader, BlockBuilder blocks, InlineFormat format)
        {
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        ReadElement(reader, blocks, format);
                        break;

                    case XmlNodeType.Text:
                    case XmlNodeType.SignificantWhitespace:
                    case XmlNodeType.Whitespace:
                    case XmlNodeType.CDATA:
                        blocks.AppendText(reader.Value, format);
                        break;

                    case XmlNodeType.EndElement:
                        return;
                }
            }
        }

        private void ReadElement(XmlReader reader, BlockBuilder blocks, InlineFormat format)
        {
            string name = reader.LocalName.ToLowerInvariant();
            bool empty = reader.IsEmptyElement;

            switch (name)
            {
                case "article":
                    if (!empty) ReadNodes(reader, blocks, format);
                    return;

                case "br":
                    blocks.AppendLineBreak();
                    return;

                case "hr":
                    blocks.Flush();
                    blocks.Add(HorizontalRule());
                    return;

                case "img":
                {
                    string src = reader.GetAttribute("src");
                    string alt = reader.GetAttribute("alt");
                    if (!empty) ReadNodes(reader, new BlockBuilder(this), format);
                    blocks.Flush();
                    Block image = BuildImage(src, alt);
                    if (image != null) blocks.Add(image);
                    return;
                }

                case "h1":
                case "h2":
                case "h3":
                case "h4":
                {
                    blocks.Flush();
                    var heading = new BlockBuilder(this);
                    if (!empty) ReadNodes(reader, heading, format);
                    heading.Flush();
                    blocks.Add(Heading(heading, name));
                    return;
                }

                case "ul":
                case "ol":
                {
                    blocks.Flush();
                    var list = new List
                    {
                        MarkerStyle = name == "ol" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                        Margin = new Thickness(14, 2, 0, 6),
                        Padding = new Thickness(0)
                    };
                    if (!empty) ReadListItems(reader, list, format);
                    if (list.ListItems.Count > 0) blocks.Add(list);
                    return;
                }

                case "table":
                {
                    blocks.Flush();
                    Block table = null;
                    if (!empty) table = BuildTable(reader, format);
                    if (table != null) blocks.Add(table);
                    return;
                }

                case "blockquote":
                {
                    blocks.Flush();
                    var quote = new BlockBuilder(this);
                    if (!empty) ReadNodes(reader, quote, format);
                    quote.Flush();
                    var section = new Section
                    {
                        BorderBrush = ThemeBrush("AccentBrush", Brushes.Gray),
                        BorderThickness = new Thickness(2, 0, 0, 0),
                        Padding = new Thickness(8, 0, 0, 0),
                        Margin = new Thickness(0, 4, 0, 6)
                    };
                    foreach (Block b in quote.Blocks) section.Blocks.Add(b);
                    blocks.Add(section);
                    return;
                }

                // Блочные контейнеры: абзац закрываем, содержимое продолжаем читать.
                case "p":
                case "div":
                case "section":
                case "details":
                    blocks.Flush();
                    if (!empty) ReadNodes(reader, blocks, format);
                    blocks.Flush();
                    return;

                // Заголовок раскрывающегося блока: схлопывания в FlowDocument нет,
                // поэтому показываем его подзаголовком, содержимое остаётся видимым.
                case "summary":
                {
                    blocks.Flush();
                    var summary = new BlockBuilder(this);
                    if (!empty) ReadNodes(reader, summary, format);
                    summary.Flush();
                    blocks.Add(Heading(summary, "h4"));
                    return;
                }

                case "b":
                case "strong":
                    if (!empty) ReadNodes(reader, blocks, format.With(bold: true));
                    return;

                case "i":
                case "em":
                    if (!empty) ReadNodes(reader, blocks, format.With(italic: true));
                    return;

                case "u":
                    if (!empty) ReadNodes(reader, blocks, format.With(underline: true));
                    return;

                case "code":
                    if (!empty) ReadNodes(reader, blocks, format.With(monospace: true));
                    return;

                case "pre":
                    blocks.Flush();
                    if (!empty) ReadNodes(reader, blocks, format.With(monospace: true));
                    blocks.Flush();
                    return;

                case "a":
                {
                    string href = reader.GetAttribute("href");
                    if (!empty) ReadNodes(reader, blocks, format.WithLink(href));
                    return;
                }

                default:
                    // span и всё прочее: разметка не важна, текст важен.
                    if (!empty) ReadNodes(reader, blocks, format);
                    return;
            }
        }

        private void ReadListItems(XmlReader reader, List list, InlineFormat format)
        {
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement)
                    return;

                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                if (!string.Equals(reader.LocalName, "li", StringComparison.OrdinalIgnoreCase))
                {
                    if (!reader.IsEmptyElement) ReadNodes(reader, new BlockBuilder(this), format);
                    continue;
                }

                bool empty = reader.IsEmptyElement;
                var item = new BlockBuilder(this);
                if (!empty) ReadNodes(reader, item, format);
                item.Flush();

                var listItem = new ListItem();
                foreach (Block b in item.Blocks) listItem.Blocks.Add(b);
                if (listItem.Blocks.Count == 0) listItem.Blocks.Add(new Paragraph());
                list.ListItems.Add(listItem);
            }
        }

        private Block BuildTable(XmlReader reader, InlineFormat format)
        {
            var rows = new List<List<BlockBuilder>>();
            int maxCells = 0;

            using (XmlReader subtree = reader.ReadSubtree())
            {
                subtree.Read(); // сам <table>
                while (subtree.Read())
                {
                    if (subtree.NodeType != XmlNodeType.Element)
                        continue;
                    if (!string.Equals(subtree.LocalName, "tr", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (subtree.IsEmptyElement)
                        continue;

                    var cells = new List<BlockBuilder>();
                    using (XmlReader rowReader = subtree.ReadSubtree())
                    {
                        rowReader.Read();
                        while (rowReader.Read())
                        {
                            if (rowReader.NodeType != XmlNodeType.Element)
                                continue;

                            string cellName = rowReader.LocalName.ToLowerInvariant();
                            if (cellName != "td" && cellName != "th")
                                continue;

                            bool header = cellName == "th";
                            var cell = new BlockBuilder(this);
                            if (!rowReader.IsEmptyElement)
                                ReadNodes(rowReader, cell, format.With(bold: header));
                            cell.Flush();
                            cells.Add(cell);
                        }
                    }

                    if (cells.Count == 0)
                        continue;

                    if (cells.Count > maxCells) maxCells = cells.Count;
                    rows.Add(cells);
                }
            }

            if (rows.Count == 0)
                return null;

            // Панель узкая: больше четырёх колонок в ней всё равно нечитаемо.
            if (maxCells > 4)
            {
                return new Paragraph(new Run("Широкая таблица — смотрите статью в вики."))
                {
                    Foreground = ThemeBrush("MutedBrush", Brushes.Gray),
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 4, 0, 6)
                };
            }

            var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 8) };
            for (int i = 0; i < maxCells; i++)
                table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });

            var group = new TableRowGroup();
            Brush border = ThemeBrush("MutedBrush", Brushes.Gray);

            foreach (List<BlockBuilder> cells in rows)
            {
                var row = new TableRow();
                foreach (BlockBuilder cell in cells)
                {
                    var tableCell = new TableCell
                    {
                        BorderBrush = border,
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        Padding = new Thickness(4, 3, 4, 3)
                    };
                    foreach (Block b in cell.Blocks) tableCell.Blocks.Add(b);
                    if (tableCell.Blocks.Count == 0) tableCell.Blocks.Add(new Paragraph());
                    row.Cells.Add(tableCell);
                }
                group.Rows.Add(row);
            }

            table.RowGroups.Add(group);
            return table;
        }

        private Block BuildImage(string src, string alt)
        {
            if (string.IsNullOrEmpty(src))
                return null;

            byte[] bytes = null;
            if (_assetLoader != null)
            {
                try { bytes = _assetLoader(src); }
                catch (Exception) { bytes = null; }
            }

            if (bytes == null || bytes.Length == 0)
            {
                if (string.IsNullOrEmpty(alt))
                    return null;

                return new Paragraph(new Run(alt))
                {
                    Foreground = ThemeBrush("MutedBrush", Brushes.Gray),
                    FontStyle = FontStyles.Italic
                };
            }

            BitmapImage bitmap;
            try
            {
                // Сначала узнаём натуральную ширину, не декодируя пиксели.
                // DecodePixelWidth задаёт ширину жёстко: если поставить его
                // безусловно, иконка 64 px раздувается до потолка и занимает
                // всю ширину панели. Поэтому сужаем только то, что шире потолка.
                int decodeWidth = 0;
                try
                {
                    using (var probe = new MemoryStream(bytes))
                    {
                        BitmapDecoder decoder = BitmapDecoder.Create(
                            probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                        if (decoder.Frames.Count > 0 && decoder.Frames[0].PixelWidth > _imageMaxWidth)
                            decodeWidth = _imageMaxWidth;
                    }
                }
                catch (Exception)
                {
                    decodeWidth = 0; // не удалось прочитать метаданные — декодируем как есть
                }

                // OnLoad обязателен: без него WPF декодирует лениво и держит поток,
                // а ObjectDisposedException выстрелит потом, во время рендера.
                using (var memory = new MemoryStream(bytes))
                {
                    bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = memory;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    if (decodeWidth > 0)
                        bitmap.DecodePixelWidth = decodeWidth;
                    bitmap.EndInit();
                }
                bitmap.Freeze();
            }
            catch (Exception)
            {
                return null;
            }

            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 2, 0, 2)
            };

            if (!string.IsNullOrEmpty(alt))
                image.ToolTip = alt;

            // BlockUIContainer, а не InlineUIContainer: скриншоты плохо
            // выравниваются по базовой линии текста.
            return new BlockUIContainer(image) { Margin = new Thickness(0, 4, 0, 8) };
        }

        private Block Heading(BlockBuilder content, string tag)
        {
            double size;
            switch (tag)
            {
                case "h1": size = 17; break;
                case "h2": size = 15; break;
                case "h3": size = 13.5; break;
                default: size = 12.5; break;
            }

            var paragraph = new Paragraph
            {
                FontSize = size,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, tag == "h1" ? 0 : 10, 0, 4),
                Foreground = ThemeBrush("PrimaryTextBrush", Brushes.Black)
            };

            foreach (Block block in content.Blocks)
            {
                var source = block as Paragraph;
                if (source == null)
                    continue;

                var inlines = new List<Inline>();
                foreach (Inline inline in source.Inlines) inlines.Add(inline);
                source.Inlines.Clear();
                foreach (Inline inline in inlines) paragraph.Inlines.Add(inline);
            }

            return paragraph;
        }

        private Block HorizontalRule()
        {
            var line = new System.Windows.Shapes.Rectangle
            {
                Height = 1,
                Fill = ThemeBrush("MutedBrush", Brushes.Gray),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            return new BlockUIContainer(line) { Margin = new Thickness(0, 6, 0, 6) };
        }

        private Brush ThemeBrush(string key, Brush fallback)
        {
            if (_theme != null)
            {
                object value = _theme[key];
                var brush = value as Brush;
                if (brush != null)
                    return brush;
            }
            return fallback;
        }


        internal void RaiseLinkClicked(string href)
        {
            EventHandler<string> handler = LinkClicked;
            if (handler != null)
                handler(this, href);
        }

        // ------------------------------------------------------------------
        // Формат текущего инлайна
        // ------------------------------------------------------------------
        private struct InlineFormat
        {
            public bool Bold;
            public bool Italic;
            public bool Underline;
            public bool Monospace;
            public string Href;

            public InlineFormat With(bool bold = false, bool italic = false, bool underline = false, bool monospace = false)
            {
                return new InlineFormat
                {
                    Bold = Bold || bold,
                    Italic = Italic || italic,
                    Underline = Underline || underline,
                    Monospace = Monospace || monospace,
                    Href = Href
                };
            }

            public InlineFormat WithLink(string href)
            {
                return new InlineFormat
                {
                    Bold = Bold,
                    Italic = Italic,
                    Underline = Underline,
                    Monospace = Monospace,
                    Href = string.IsNullOrEmpty(href) ? Href : href
                };
            }
        }

        // ------------------------------------------------------------------
        // Накопитель блоков: собирает инлайны в абзац и сворачивает пробелы
        // по правилам HTML, иначе каждый абзац получает ведущий отступ.
        // ------------------------------------------------------------------
        private sealed class BlockBuilder
        {
            private readonly HtmlFlowDocumentRenderer _owner;
            private readonly List<Block> _blocks = new List<Block>();
            private Paragraph _paragraph;
            private bool _lastWasSpace = true;

            public BlockBuilder(HtmlFlowDocumentRenderer owner)
            {
                _owner = owner;
            }

            public IEnumerable<Block> Blocks { get { return _blocks; } }

            public void Add(Block block)
            {
                Flush();
                _blocks.Add(block);
            }

            public void AppendText(string text, InlineFormat format)
            {
                if (string.IsNullOrEmpty(text))
                    return;

                string collapsed = Collapse(text, ref _lastWasSpace);
                if (collapsed.Length == 0)
                    return;

                Inline inline = BuildInline(collapsed, format);
                Current().Inlines.Add(inline);
            }

            public void AppendLineBreak()
            {
                Current().Inlines.Add(new LineBreak());
                _lastWasSpace = true;
            }

            public void Flush()
            {
                if (_paragraph == null)
                    return;

                if (_paragraph.Inlines.Count > 0)
                    _blocks.Add(_paragraph);

                _paragraph = null;
                _lastWasSpace = true;
            }

            private Paragraph Current()
            {
                if (_paragraph == null)
                {
                    _paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 6) };
                    _lastWasSpace = true;
                }
                return _paragraph;
            }

            private Inline BuildInline(string text, InlineFormat format)
            {
                var run = new Run(text);

                if (format.Monospace)
                    run.FontFamily = new FontFamily("Consolas");

                Inline inline = run;

                if (!string.IsNullOrEmpty(format.Href))
                {
                    var link = new Hyperlink(run)
                    {
                        Foreground = _owner.ThemeBrush("AccentBrush", Brushes.RoyalBlue),
                        TextDecorations = TextDecorations.Underline
                    };

                    string href = format.Href;
                    // NavigateUri сам по себе ничего не делает — нужен обработчик.
                    link.Click += (s, e) => _owner.RaiseLinkClicked(href);
                    inline = link;
                }

                if (format.Bold) inline.FontWeight = FontWeights.SemiBold;
                if (format.Italic) inline.FontStyle = FontStyles.Italic;
                if (format.Underline && string.IsNullOrEmpty(format.Href))
                    inline.TextDecorations = TextDecorations.Underline;

                return inline;
            }

            /// <summary>Серии пробелов в один, обрезка на границах блока.</summary>
            private static string Collapse(string text, ref bool lastWasSpace)
            {
                var builder = new StringBuilder(text.Length);
                foreach (char c in text)
                {
                    bool isSpace = c == ' ' || c == '\t' || c == '\r' || c == '\n'
                                   || c == ' ' || c == ' ' || c == ' ';
                    if (isSpace)
                    {
                        if (lastWasSpace) continue;
                        builder.Append(' ');
                        lastWasSpace = true;
                    }
                    else
                    {
                        builder.Append(c);
                        lastWasSpace = false;
                    }
                }
                return builder.ToString();
            }
        }
    }
}
