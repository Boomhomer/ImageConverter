using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OpenCvSharp;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Ookii.Dialogs.Wpf;

namespace ImageConverter
{
    public partial class MainWindow : System.Windows.Window
    {
        private ObservableCollection<string> logEntries = new ObservableCollection<string>();
        private bool formatChangedManually = false;

        public MainWindow()
        {
            InitializeComponent();
            lstLogs.ItemsSource = logEntries;
            sliderQuality.ValueChanged += (s, e) => txtQuality.Text = ((int)sliderQuality.Value).ToString();
        }

        private void BrowseFolder(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowserDialog();
            if (dialog.ShowDialog() == true)
            {
                txtFolderPath.Text = dialog.SelectedPath;
                LoadImagesFromFolder(dialog.SelectedPath);
            }
        }

        private void FolderPathChanged(object sender, TextChangedEventArgs e)
        {
            if (Directory.Exists(txtFolderPath.Text))
            {
                LoadImagesFromFolder(txtFolderPath.Text);
            }

            btnConvert.IsEnabled = Directory.Exists(txtFolderPath.Text) && Directory.Exists(txtOutputFolderPath.Text);
        }

        private void BrowseOutputFolder(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowserDialog();
            if (dialog.ShowDialog() == true)
            {
                txtOutputFolderPath.Text = dialog.SelectedPath;
            }
        }

        private void LoadImagesFromFolder(string folderPath)
        {
            logEntries.Clear();
            string[] supportedExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };
            foreach (var file in Directory.EnumerateFiles(folderPath).Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLower())))
            {
                logEntries.Add($"📂 Načten: {Path.GetFileName(file)}");
            }
            btnConvert.IsEnabled = logEntries.Count > 0;
        }

        private async void ConvertImages(object sender, RoutedEventArgs e)
        {
            btnConvert.IsEnabled = false;
            progressBar.Visibility = Visibility.Visible;
            progressBar.Value = 0;
            txtProgress.Text = "0 %";
            txtStatus.Text = "⏳ Konverze probíhá...";

            if (string.IsNullOrWhiteSpace(txtOutputFolderPath.Text))
            {
                MessageBox.Show("Vyberte výstupní složku!", "Chyba", MessageBoxButton.OK, MessageBoxImage.Warning);
                btnConvert.IsEnabled = true;
                progressBar.Visibility = Visibility.Hidden;
                return;
            }

            string outputFolder = txtOutputFolderPath.Text;
            Directory.CreateDirectory(outputFolder);

            bool autoCrop = chkAutoCrop.IsChecked == true;
            bool resizeByLongestSide = chkResizeByLongestSide.IsChecked == true;
            bool replaceAlpha = chkReplaceAlpha.IsChecked == true;
            int maxSize = int.TryParse(txtMaxSize.Text, out int size) ? size : 0;
            string selectedColor = (cmbAlphaColor?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Bílá";
            string selectedFormat = (cmbFormat?.SelectedItem as ComboBoxItem)?.Content?.ToString().ToLower() ?? "jpg";

            var imageFiles = logEntries.Where(entry => entry.StartsWith("📂")).Select(entry => entry.Split(": ")[1]).ToList();
            int total = imageFiles.Count;

            if (total == 0)
            {
                MessageBox.Show("Nebyl nalezen žádný obrázek ke konverzi!", "Chyba", MessageBoxButton.OK, MessageBoxImage.Warning);
                btnConvert.IsEnabled = true;
                progressBar.Visibility = Visibility.Hidden;
                return;
            }

            for (int i = 0; i < total; i++)
            {
                string fileName = imageFiles[i];
                string filePath = Path.Combine(txtFolderPath.Text, fileName);
                string savePath = Path.Combine(outputFolder, $"{Path.GetFileNameWithoutExtension(fileName)}.{selectedFormat}");

                Mat img = Cv2.ImRead(filePath, ImreadModes.Unchanged);

                if (autoCrop)
                {
                    img = AutoCropImage(img);
                }

                if (replaceAlpha && img.Channels() == 4)
                {
                    img = ReplaceTransparency(img, selectedColor);
                }

                if (maxSize > 0 && resizeByLongestSide)
                {
                    img = ResizeImage(img, maxSize, resizeByLongestSide);
                }

                bool success = Cv2.ImWrite(savePath, img);
                logEntries.Add(success ? $"✅ Uloženo: {savePath}" : $"❌ Chyba při ukládání: {fileName}");

                double progress = ((double)(i + 1) / total) * 100;
                progressBar.Dispatcher.Invoke(() => progressBar.Value = progress);
                txtProgress.Dispatcher.Invoke(() => txtProgress.Text = $"{progress:0}%");

                await Task.Delay(100); // Simulace průběhu
            }

            btnConvert.IsEnabled = true;
            txtStatus.Text = "✅ Konverze dokončena!";
            progressBar.Visibility = Visibility.Hidden;
        }

        private void FormatChanged(object sender, SelectionChangedEventArgs e)
        {
            if (formatChangedManually)
            {
                logEntries.Add("📌 Formát změněn.");
            }
            formatChangedManually = true;
        }

        private Mat ResizeImage(Mat img, int maxSize, bool resizeByLongestSide)
        {
            int width = img.Width;
            int height = img.Height;
            double scale = resizeByLongestSide
                ? maxSize / (double)Math.Max(width, height)
                : maxSize / (double)Math.Min(width, height);

            if (scale >= 1) return img;

            int newWidth = (int)(width * scale);
            int newHeight = (int)(height * scale);

            Mat resized = new Mat();
            Cv2.Resize(img, resized, new OpenCvSharp.Size(newWidth, newHeight), 0, 0, InterpolationFlags.Lanczos4);
            return resized;
        }

        private Mat ReplaceTransparency(Mat img, string color)
        {
            if (img.Empty() || img.Channels() < 4)
                return img;

            Mat[] channels = new Mat[4];
            Cv2.Split(img, out channels);
            Mat bgr = new Mat();
            Cv2.Merge(new Mat[] { channels[0], channels[1], channels[2] }, bgr);
            Mat alpha = channels[3];

            Scalar replacementColor = color switch
            {
                "Černá" => new Scalar(0, 0, 0),
                "Šedá" => new Scalar(128, 128, 128),
                "Modrá" => new Scalar(255, 0, 0),
                "Červená" => new Scalar(0, 0, 255),
                "Zelená" => new Scalar(0, 255, 0),
                _ => new Scalar(255, 255, 255)
            };

            Mat alphaFloat = new Mat();
            alpha.ConvertTo(alphaFloat, MatType.CV_32FC1, 1.0 / 255.0);
            Mat alphaFloat3C = new Mat();
            Cv2.Merge(new Mat[] { alphaFloat, alphaFloat, alphaFloat }, alphaFloat3C);
            Mat background = new Mat(bgr.Size(), MatType.CV_32FC3, replacementColor);
            bgr.ConvertTo(bgr, MatType.CV_32FC3);

            Mat blended = new Mat();
            Cv2.Multiply(alphaFloat3C, bgr, bgr);
            Cv2.Multiply(Scalar.All(1.0) - alphaFloat3C, background, background);
            Cv2.Add(bgr, background, blended);

            blended.ConvertTo(blended, MatType.CV_8UC3);
            return blended;
        }

        private Mat AutoCropImage(Mat img)
        {
            Mat gray = new Mat();
            Cv2.CvtColor(img, gray, ColorConversionCodes.BGRA2GRAY);
            Cv2.Threshold(gray, gray, 240, 255, ThresholdTypes.BinaryInv);

            Cv2.FindContours(gray, out OpenCvSharp.Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            if (contours.Length == 0) return img;

            OpenCvSharp.Rect boundingBox = Cv2.BoundingRect(contours[0]);

            foreach (var contour in contours)
            {
                boundingBox = boundingBox.Union(Cv2.BoundingRect(contour));
            }

            return new Mat(img, boundingBox);
        }
    }
}
