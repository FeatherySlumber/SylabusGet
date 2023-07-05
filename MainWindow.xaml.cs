using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace ClassTimetableToSyllabus
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = VM;
        }

        private readonly VM VM = new();

        private void Select_Alpha_Kamoku(object sender, RoutedEventArgs e)
        {
            KamokuList.SelectedItems.Clear();
            for (int i = 0; i < KamokuList.Items.Count; i++)
            {
                if (KamokuList.Items[i] is Kamoku kamoku)
                {
                    if (Regex.IsMatch(kamoku.Name, "^[a-zA-Z0-9 ]*$"))
                    {
                        KamokuList.SelectedItems.Add(KamokuList.Items[i]);
                    }
                }
            }
        }
    }

    public class VM : INotifyPropertyChanged
    {
        public VM()
        {
            PdfLoadCommand = new(_ =>
            {
                OpenFileDialog fileDialog = new()
                {
                    Filter = "PDF|*.pdf"
                };

                if (fileDialog.ShowDialog() == true)
                {
                    Kamokus.Clear();
                    PdfDocument = PdfDocument.Open(fileDialog.FileName);
                    PdfName = Path.GetFileNameWithoutExtension(fileDialog.FileName);
                }
            }, _ => Kamokus.Count == 0);

            PdfUnloadCommand = new(_ =>
            {
                Kamokus.Clear();
                PdfDocument?.Dispose();
                PdfDocument = null;
                PdfName = string.Empty;
            }, _ => PdfDocument is not null);

            PdfSplitCommand = new(_ =>
            {
                Kamokus.Clear();
                int cnt = 1;
#pragma warning disable CS8602 // null 参照の可能性があるものの逆参照です。
                foreach (Page page in PdfDocument.GetPages()) // CanExecuteで潰している
                {
                    foreach (Word word in page.GetWords())
                    {
                        if (word.Text.Contains(PdfGroupingText))
                        {
                            Kamokus.Add(new(PdfDocument) { StartPage = cnt });
                            break;
                        }
                    }
                    if (Syllabus.Count > 0)
                    {
                        Syllabus[^1].PageCount++;
                    }

                    cnt++;
                }
#pragma warning restore CS8602 // null 参照の可能性があるものの逆参照です。
            }, _ => PdfDocument is not null);

            // 科目名を抜きたい
            NameExecuteCommand = new(_ =>
            {
                if (PdfDocument is null) return;

                for (int i = 0; i < Syllabus.Count; i++)
                {
                    Syllabus[i].ClearName();

                    Page page = Syllabus[i].GetPage(PdfDocument, 0);

                    if (!page.Text.Contains(PdfNameGroupingText)) continue;
                    var hLocation = page.GetWords().Where(w => w.Text.Contains(PdfNameGroupingText)).First().Letters[0].GlyphRectangle.Top;
                    foreach (Word word in page.GetWords())
                    {
                        if (word.Letters[0].Location.Y <= hLocation) continue;

                        // "講義科目名称："など'：'を含む要素の除去
                        if (IsExecuteRemoveStr)
                        {
                            bool flag = false;
                            foreach (char c in RemoveTargets)
                            {
                                flag |= word.Text.Contains(c);
                            }
                            if (flag) continue;
                        }
                        // 科目コードの除去
                        if (IsExecuteRemoveDigits)
                        {
                            Regex regex = new($@"[0-9]{{{IntDigitsExclusion}}}");
                            if (regex.IsMatch(word.Text)) continue;
                        }
                        Syllabus[i].AddName(word.Text);
                    }
                }
            }, _ => Syllabus.Count != 0);

            // 〇年〇期と一致するテキストを抜きたい
            PeriodExecuteCommand = new(_ =>
            {
                if (PdfDocument is null) return;

                for (int i = 0; i < Syllabus.Count; i++)
                {
                    Page page = Syllabus[i].GetPage(PdfDocument, 0);

                    if (!page.Text.Contains(PdfNameGroupingText)) continue;
                    double overLocation = page.GetWords().Where(w => w.Text.Contains(PeriodOverText)).First().Letters[0].GlyphRectangle.Bottom;
                    double underLocation = page.GetWords().Where(w => w.Text.Contains(PeriodUnderText)).First().Letters[0].GlyphRectangle.Top;
                    List<Word> words = new();
                    foreach (Word word in page.GetWords())
                    {
                        if (word.Letters[0].Location.Y >= overLocation) continue;
                        if (word.Letters[0].Location.Y <= underLocation) continue;
                        words.Add(word);
                    }
                    Syllabus[i].Period = words.OrderBy(w => w.Letters[0].Location.X).ToArray()[PeriodIndexText].Text;
                }
            }, _ => Syllabus.Count != 0);

            // 〇〇-〇〇-○○と一致するテキストを抜きたい
            CodeExecuteCommand = new(_ =>
            {
                if (PdfDocument is null) return;

                for (int i = 0; i < Syllabus.Count; i++)
                {
                    Syllabus[i].Numbering.Clear();

                    Page page = Syllabus[i].GetPage(PdfDocument, 0);

                    if (!page.Text.Contains(PdfNameGroupingText)) continue;
                    var hLocation = page.GetWords().Where(w => w.Text.Contains(CodeUnderText)).First().Letters[0].GlyphRectangle.Top;

                    foreach (Word word in page.GetWords())
                    {
                        if (word.Letters[0].Location.Y <= hLocation) continue;

                        // 途中で改行が入るナンバリング対策
                        if (IsCodeJoin)
                        {
                            if (word.Text.Contains(CodeAddText))
                            {
                                if (word.Text[^1] == CodeJoinChar)
                                {
                                    string text = word.Text;
                                    string[] temp = page.Text.Split(' ');
                                    for (int j = 0; j < temp.Length; j++)
                                    {
                                        if (temp[j] != word.Text) continue;

                                        for (int k = j + 1; k < temp.Length; k++)
                                        {
                                            if (string.IsNullOrWhiteSpace(temp[k])) continue;

                                            text += temp[k];
                                            break;
                                        }
                                        break;
                                    }
                                    Syllabus[i].Numbering.Add(text);
                                }
                                else
                                {
                                    Syllabus[i].Numbering.Add(word.Text);
                                }
                            }
                        }
                        else
                        {
                            if (word.Text.Contains(CodeAddText))
                            {
                                Syllabus[i].Numbering.Add(word.Text);
                            }
                        }

                        // 途中で改行が入るナンバリング対策
                        if (IsCodeContainRemove)
                        {
                            List<string> temp = new();
                            foreach (string code in Syllabus[i].Numbering)
                            {
                                foreach (string c in Syllabus[i].Numbering)
                                {
                                    if (!ReferenceEquals(code, c) && code.Contains(c))
                                    {
                                        temp.Add(c);
                                    }
                                }
                            }
                            foreach (string t in temp)
                            {
                                Syllabus[i].Numbering.Remove(t);
                            }
                        }
                    }
                }
            }, _ => Syllabus.Count != 0);

            NameJoinCommand = new(param =>
            {
                if (param is IEnumerable<object> kamokus)
                {
                    foreach (Kamoku kamoku in kamokus.Cast<Kamoku>())
                    {
                        kamoku.AddName(string.Join(' ', kamoku.NameList));
                    }
                }
            }, param =>
            {
                if (param is IEnumerable<object> kamokus)
                {
                    int cnt = 0;
                    foreach (Kamoku kamoku in kamokus.Cast<Kamoku>())
                    {
                        cnt++;
                    }
                    return cnt != 0;
                }
                return false;
            });

            JsonOutputCommand = new(_ =>
            {
                string path = Path.Combine("Output", PdfName);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                using Stream stream = new FileStream(Path.Combine(path, PdfName + ".json"), FileMode.Create, FileAccess.Write);
                Kamokus.JsonOutput(stream, FileNameConf);

            }, _ => 
            {
                if (Syllabus.Count == 0) return false;
                return Syllabus.Any(x => x.NameList.Count == 0) is false;
            });

            PdfOutputCommand = new(_ =>
            {
                if (PdfDocument is null) return;

                string path = Path.Combine("Output", PdfName);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                Kamokus.PdfOutput(PdfDocument, path, FileNameConf);
            }, _ =>
            {
                if (Syllabus.Count == 0) return false;
                return Syllabus.Any(x => x.NameList.Count == 0) is false;
            });

            FileNameConfList = Enum.GetValues<FileNameConfig>().Select(x => new Tuple<FileNameConfig, string>(x, x.GetItemName())).ToArray();
        }
        ~VM(){
            PdfDocument?.Dispose();
        }
        public PdfDocument? PdfDocument = null;

        private string pdfName = "ソースPDF";
        public string PdfName
        {
            get => pdfName; set
            {
                pdfName = value;
                NotifyPropertyChanged(nameof(PdfName));
            }
        }

        private string pdfGroupingText = "講義科目名称";
        public string PdfGroupingText
        {
            get => pdfGroupingText;
            set
            {
                pdfGroupingText = value;
                NotifyPropertyChanged(nameof(PdfGroupingText));
            }
        }

        private string pdfNameGroupingText = "英文科目名称";
        public string PdfNameGroupingText
        {
            get => pdfNameGroupingText;
            set
            {
                pdfNameGroupingText = value;
                NotifyPropertyChanged(nameof(PdfNameGroupingText));
            }
        }
        public char[] RemoveTargets { get; private set; } = new char[] { '-', '：' };
        public string RemoveTarget
        {
            get => new(RemoveTargets);
            set
            {
                RemoveTargets = value.ToCharArray();
                NotifyPropertyChanged(nameof(RemoveTarget));
            }
        }

        private bool isExecuteRemoveStr = true;
        public bool IsExecuteRemoveStr
        {
            get => isExecuteRemoveStr; set
            {
                isExecuteRemoveStr = value;
                NotifyPropertyChanged(nameof(IsExecuteRemoveStr));
            }
        }

        private bool isExecuteRemoveDigits = true;
        public bool IsExecuteRemoveDigits
        {
            get => isExecuteRemoveDigits; set
            {
                isExecuteRemoveDigits = value;
                NotifyPropertyChanged(nameof(IsExecuteRemoveDigits));
            }
        }


        private int intDigitsExclusion = 5;
        public int IntDigitsExclusion
        {
            get => intDigitsExclusion;
            set
            {
                intDigitsExclusion = value;
                NotifyPropertyChanged(nameof(IntDigitsExclusion));
            }
        }

        private string periodOverText = "開講期間";
        public string PeriodOverText
        {
            get => periodOverText;
            set
            {
                periodOverText = value;
                NotifyPropertyChanged(nameof(PeriodOverText));
            }
        }

        private string periodUnderText = "担当教員";
        public string PeriodUnderText
        {
            get => periodUnderText;
            set
            {
                periodUnderText = value;
                NotifyPropertyChanged(nameof(PeriodUnderText));
            }
        }

        private int periodIndexText = 0;
        public int PeriodIndexText
        {
            get => periodIndexText;
            set
            {
                periodIndexText = value;
                NotifyPropertyChanged(nameof(PeriodIndexText));
            }
        }

        private string codeUnderText = "英文科目名称"; 
        public string CodeUnderText
        {
            get => codeUnderText; set
            {
                codeUnderText = value;
                NotifyPropertyChanged(nameof(CodeUnderText));
            }
        }
        private string codeAddText = "-";
        public string CodeAddText
        {
            get => codeAddText; set
            {
                codeAddText = value;
                NotifyPropertyChanged(nameof(CodeAddText));
            }
        }


        private char codeJoinChar = '-';
        public char CodeJoinChar
        {
            get => codeJoinChar; set
            {
                codeJoinChar = value;
                NotifyPropertyChanged(nameof(CodeJoinChar));
            }
        }

        private bool isCodeJoin = true; 
        public bool IsCodeJoin
        {
            get => isCodeJoin; set
            {
                isCodeJoin = value;
                NotifyPropertyChanged(nameof(IsCodeJoin));
            }
        }

        private bool isCodeContainRemove = true;

        public bool IsCodeContainRemove
        {
            get => isCodeContainRemove; set
            {
                isCodeContainRemove = value;
                NotifyPropertyChanged(nameof(IsCodeContainRemove));
            }
        }

        private FileNameConfig fileNameConf = FileNameConfig.Name;
        public FileNameConfig FileNameConf
        {
            get => fileNameConf; set
            {
                fileNameConf = value;
                NotifyPropertyChanged(nameof(FileNameConf));
            }
        }

        public IReadOnlyCollection<Tuple<FileNameConfig, string>> FileNameConfList { get; private init; }

        public CommandBase PdfLoadCommand { get; private init; }
        public CommandBase PdfUnloadCommand { get; private init; }

        public CommandBase PdfSplitCommand { get; private init; }

        public CommandBase NameExecuteCommand { get; private init; }
        public CommandBase PeriodExecuteCommand { get; private init; }
        public CommandBase CodeExecuteCommand { get; private init; }

        public CommandBase JsonOutputCommand { get; private init; }
        public CommandBase PdfOutputCommand { get; private init; }

        public CommandBase NameJoinCommand { get; private init; } 

        public ReadOnlyObservableCollection<Kamoku> Syllabus => Kamokus.Kamokus;
        private readonly Syllabus Kamokus = new();

        protected void NotifyPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new(propertyName));

        public event PropertyChangedEventHandler? PropertyChanged;
    }


    public class CommandBase : ICommand
    {
        private readonly Action<object?> action;
        private readonly Func<object?, bool> canExecute = (_) => true;
        public CommandBase(Action<object?> act) => action = act;
        public CommandBase(Action<object?> act, Func<object?, bool> canExe)
        {
            action = act;
            canExecute = canExe;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => canExecute(parameter);

        public void Execute(object? parameter) => action(parameter);
    }
}
