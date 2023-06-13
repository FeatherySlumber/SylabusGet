using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
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

        private void PDF_Load(object sender, RoutedEventArgs e)
        {
            OpenFileDialog fileDialog = new()
            {
                Filter = "PDF|*.pdf"
            };

            if (fileDialog.ShowDialog() == true)
            {
                VM.Syllabus.Clear();
                VM.PdfDocument = PdfDocument.Open(fileDialog.FileName);
                VM.PdfName = Path.GetFileNameWithoutExtension(fileDialog.FileName);
            }
        }

        private void PDF_Split_Button(object sender, RoutedEventArgs e)
        {
            VM.Syllabus.Clear();
            int cnt = 1;
            foreach (Page page in VM.PdfDocument.GetPages())
            {
                foreach (Word word in page.GetWords())
                {
                    if (word.Text.Contains(VM.PdfGroupingText))
                    {
                        VM.Syllabus.Add(new(VM.PdfDocument) { StartPage = cnt });
                        break;
                    }
                }
                if (VM.Syllabus.Count > 0)
                {
                    VM.Syllabus[^1].PageCount++;
                }
                // using StreamWriter stream = new(cnt.ToString() + ".txt", new FileStreamOptions() { Mode = FileMode.Create, Access = FileAccess.Write });
                // stream.WriteLine(page.Text);

                cnt++;
            }
        }

        // 科目名を抜きたい
        private void Name_Execute_Button(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < VM.Syllabus.Count; i++)
            {
                VM.Syllabus[i].ClearName();

                Page page = VM.Syllabus[i].GetPage(VM.PdfDocument, 0);

                if (!page.Text.Contains(VM.PdfNameGroupingText)) continue;
                var hLocation = page.GetWords().Where(w => w.Text.Contains(VM.PdfNameGroupingText)).First().Letters[0].GlyphRectangle.Top;
                foreach (Word word in page.GetWords())
                {
                    if (word.Letters[0].Location.Y <= hLocation) continue;

                    // "講義科目名称："など'：'を含む要素の除去
                    if (VM.IsExecuteRemoveStr)
                    {
                        bool flag = false;
                        foreach(char c in VM.RemoveTargets)
                        {
                            flag |= word.Text.Contains(c);
                        }
                        if (flag) continue;
                    }
                    // 科目コードの除去
                    if (VM.IsExecuteRemoveDigits)
                    {
                        Regex regex = new($@"[0-9]{{{VM.IntDigitsExclusion}}}");
                        if (regex.IsMatch(word.Text)) continue;
                    }
                    VM.Syllabus[i].AddName(word.Text);
                }
            }
        }

        // 〇年〇期と一致するテキストを抜きたい
        private void Period_Execute_Button(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < VM.Syllabus.Count; i++)
            {
                Page page = VM.Syllabus[i].GetPage(VM.PdfDocument, 0);

                if (!page.Text.Contains(VM.PdfNameGroupingText)) continue;
                double overLocation = page.GetWords().Where(w => w.Text.Contains(VM.PeriodOverText)).First().Letters[0].GlyphRectangle.Bottom;
                double underLocation = page.GetWords().Where(w => w.Text.Contains(VM.PeriodUnderText)).First().Letters[0].GlyphRectangle.Top;
                List<Word> words = new();
                foreach (Word word in page.GetWords())
                {
                    if (word.Letters[0].Location.Y >= overLocation) continue;
                    if (word.Letters[0].Location.Y <= underLocation) continue;
                    words.Add(word);
                }
                VM.Syllabus[i].Period = words.OrderBy(w => w.Letters[0].Location.X).ToArray()[VM.PeriodIndexText].Text;
            }

        }

        // 〇〇-〇〇-○○と一致するテキストを抜きたい
        private void Code_Execute_Button(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < VM.Syllabus.Count; i++)
            {
                VM.Syllabus[i].Numbering.Clear();

                Page page = VM.Syllabus[i].GetPage(VM.PdfDocument, 0);

                if (!page.Text.Contains(VM.PdfNameGroupingText)) continue;
                var hLocation = page.GetWords().Where(w => w.Text.Contains(VM.CodeUnderText)).First().Letters[0].GlyphRectangle.Top;

                foreach (Word word in page.GetWords())
                {
                    if (word.Letters[0].Location.Y <= hLocation) continue;

                    // 途中で改行が入るナンバリング対策
                    if (VM.IsCodeJoin)
                    {
                        if (word.Text.Contains(VM.CodeAddText))
                        {
                            if (word.Text[^1] == VM.CodeJoinChar)
                            {
                                string text = word.Text;
                                string[] temp = page.Text.Split(' ');
                                for (int j = 0; j < temp.Length; j++)
                                {
                                    if (temp[j] == word.Text)
                                    {
                                        for(int k = j + 1; k < temp.Length; k++)
                                        {
                                            if (!string.IsNullOrWhiteSpace(temp[k]))
                                            {
                                                text += temp[k];
                                                break;
                                            }
                                        }
                                        break;
                                    }
                                }
                                VM.Syllabus[i].Numbering.Add(text);
                            }
                            else
                            {
                                VM.Syllabus[i].Numbering.Add(word.Text);
                            }
                        }
                    }
                    else
                    {
                        if (word.Text.Contains(VM.CodeAddText))
                        {
                            VM.Syllabus[i].Numbering.Add(word.Text);
                        }
                    }

                    // 途中で改行が入るナンバリング対策
                    if (VM.IsCodeContainRemove)
                    {
                        List<string> temp = new();
                        foreach(string code in VM.Syllabus[i].Numbering)
                        {
                            foreach(string c in VM.Syllabus[i].Numbering)
                            {
                                if(!ReferenceEquals(code, c) && code.Contains(c))
                                {
                                    temp.Add(c);
                                }
                            }
                        }
                        foreach (string t in temp)
                        {
                            VM.Syllabus[i].Numbering.Remove(t);
                        }
                    }
                }
            }
        }

        static readonly string Output = "Output";

        private void JSON_Output_Button(object sender, RoutedEventArgs e)
        {
            if (!Directory.Exists(Output))
            {
                Directory.CreateDirectory(Output);
            }
            using Stream stream = new FileStream(Path.Combine(Output, VM.PdfName + ".json"), FileMode.Create, FileAccess.Write);
            JsonSerializer.Serialize(stream, VM.Syllabus, new JsonSerializerOptions()
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            });
        }

        private void PDF_Output_Button(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < VM.Syllabus.Count; i++)
            {
                VM.Syllabus[i].OutputPDF(VM.PdfDocument, Output);
            }
        }

        private void PDF_Unload(object sender, RoutedEventArgs e)
        {
            VM.Syllabus.Clear();
            VM.PdfDocument.Dispose();
            VM.PdfName = string.Empty;
        }
    }

    public class VM : INotifyPropertyChanged
    {
        public VM()
        {
            NameJoinCommand = new(param =>
            {
                if (param is IEnumerable<object> kamokus)
                {
                    foreach (Kamoku kamoku in kamokus.Cast<Kamoku>())
                    {
                        kamoku.AddName(string.Join(' ', kamoku.NameList));
                    }
                }
            });
        }
        ~VM(){
            pdfDocument?.Dispose();
        }
        private PdfDocument? pdfDocument = null;
        public PdfDocument PdfDocument { get => pdfDocument ?? throw new NullReferenceException(); set => pdfDocument = value; }

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

        public CommandBase NameJoinCommand { get; private init; } 

        public ObservableCollection<Kamoku> Syllabus { get; } = new();

        protected void NotifyPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new(propertyName));

        public event PropertyChangedEventHandler? PropertyChanged;
    }


    public class CommandBase : ICommand
    {
        private readonly Action<object?> action;
        public CommandBase(Action<object?> act) => action = act;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            action(parameter);
        }
    }
}
