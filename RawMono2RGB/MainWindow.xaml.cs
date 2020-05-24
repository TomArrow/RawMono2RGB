using BitMiracle.LibTiff.Classic;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Shapes = System.Windows.Shapes;
using Forms = System.Windows.Forms;

using System.Drawing;
using Imaging = System.Drawing.Imaging;
using Orientation = BitMiracle.LibTiff.Classic.Orientation;
using System.Runtime.InteropServices;
using System.Threading;

namespace RawMono2RGB
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window,  INotifyPropertyChanged
    {
        
        private const TiffTag TIFFTAG_CFAREPEATPATTERNDIM = (TiffTag)33421;
        private const TiffTag TIFFTAG_CFAPATTERN = (TiffTag)33422;

        private static Tiff.TiffExtendProc m_parentExtender;
        private BackgroundWorker worker = new BackgroundWorker();

        string sourceFolder = null;
        string targetFolder = null;
        string[] filesInSourceFolder = null;
        private int currentProgress;
        private string currentStatus;
        private static int _counter = 0;
        private static int _totalFiles = 0;

        // Declare the event
        public event PropertyChangedEventHandler PropertyChanged;

        public static void TagExtender(Tiff tif)
        {
            TiffFieldInfo[] tiffFieldInfo =
            {
                new TiffFieldInfo(TIFFTAG_CFAREPEATPATTERNDIM, 2, 2, TiffType.SHORT, FieldBit.Custom, false, false, "CFARepeatPatternDim"),
                new TiffFieldInfo(TIFFTAG_CFAPATTERN, 4, 4, TiffType.BYTE, FieldBit.Custom, false, false, "CFAPattern"),
            };

            /* Reference code copied from C++ version of LibTiff (not yet implemented in LibTiff.NET)
             *{ TIFFTAG_CFAREPEATPATTERNDIM, 2, 2, TIFF_SHORT, 0, TIFF_SETGET_C0_UINT16, TIFF_SETGET_UNDEFINED,	FIELD_CUSTOM, 0,	0,	"CFARepeatPatternDim", NULL },
	            { TIFFTAG_CFAPATTERN,	4, 4,	TIFF_BYTE, 0, TIFF_SETGET_C0_UINT8, TIFF_SETGET_UNDEFINED, FIELD_CUSTOM, 0,	0,	"CFAPattern" , NULL},
             */

            tif.MergeFieldInfo(tiffFieldInfo, tiffFieldInfo.Length);

            if (m_parentExtender != null)
                m_parentExtender(tif);
        }

        // Create the OnPropertyChanged method to raise the event
        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            // Register the custom tag handler
            Tiff.TiffExtendProc extender = TagExtender;
            m_parentExtender = Tiff.SetTagExtender(extender);
        }

        /*
        private void BtnLoadRAW_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Raw bayer files (.raw)|*.raw";
            if(ofd.ShowDialog() == true)
            {

                string fileNameWithoutExtension = Path.GetDirectoryName(ofd.FileName) + "\\" + Path.GetFileNameWithoutExtension(ofd.FileName);
                string fileName = fileNameWithoutExtension + ".dng";

                //byte[,] bayerPattern = getBayerPattern();

                ProcessRAW(ofd.FileName, fileName);
            }
        }*/

        private void ProcessRAW( string[] srcRGBTriplet,string targetFilename)
        {

            byte[] buffR = File.ReadAllBytes(srcRGBTriplet[0]);
            byte[] buffG = File.ReadAllBytes(srcRGBTriplet[1]);
            byte[] buffB = File.ReadAllBytes(srcRGBTriplet[2]);



            int width = 4096;
            int height = 3000;

            this.Dispatcher.Invoke(() =>
            {
                width = int.Parse(rawWidth.Text);
                height = int.Parse(rawHeight.Text);
            });


            // Interleave
            int pixelCount = width * height;
            int totalLength = width * height * 3;
            byte[] buff = new byte[totalLength*2];

            for(int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
            {
                buff[pixelIndex * 3 * 2 +4] = buffR[pixelIndex*2];
                buff[pixelIndex * 3 * 2 +5] = buffR[pixelIndex * 2 + 1];
                buff[pixelIndex * 3 * 2 +2] = buffG[pixelIndex * 2];
                buff[pixelIndex * 3 * 2 +3] = buffG[pixelIndex * 2 + 1];
                buff[pixelIndex * 3 * 2 ] = buffB[pixelIndex * 2];
                buff[pixelIndex * 3 * 2 +1  ] = buffB[pixelIndex * 2 + 1];
            }

            string fileName = targetFilename;

                using (Tiff output = Tiff.Open(fileName, "w"))
                {

                    output.SetField(TiffTag.SUBFILETYPE, 0);
                    // Basic TIFF functionality
                    output.SetField(TiffTag.IMAGEWIDTH, width);
                    output.SetField(TiffTag.IMAGELENGTH, height);
                    output.SetField(TiffTag.SAMPLESPERPIXEL, 3);
                    output.SetField(TiffTag.BITSPERSAMPLE, 16);
                    output.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
                    //output.SetField(TiffTag.ROWSPERSTRIP, height);
                    // output.SetField(TiffTag.COMPRESSION, Compression.ADOBE_DEFLATE);
                    output.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
                    output.SetField(TiffTag.FILLORDER, FillOrder.MSB2LSB);
                    output.SetField(TiffTag.COMPRESSION, Compression.LZW); //LZW doesn't work with DNG apparently
                    //output.SetField(TiffTag.COMPRESSION, Compression.NONE);

                    output.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);

                /*

                    // Maybe use later if necessary:
                    //output.SetField(TiffTag.SAMPLESPERPIXEL, 1);
                    //output.SetField(TiffTag.BITSPERSAMPLE, 3, bpp);
                    //output.SetField(TiffTag.LINEARIZATIONTABLE, 256, linearizationTable);
                    //output.SetField(TiffTag.WHITELEVEL, 1);
                    */

                    output.WriteEncodedStrip(0, buff, width * height * 2*3);
                }
        } 



        private void BtnLoadRAWFolder_Click(object sender, RoutedEventArgs e)
        {
            var fbd = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog();
            bool? result = fbd.ShowDialog();

            if (result == true && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
            {
                sourceFolder = fbd.SelectedPath;
                txtSrcFolder.Text = sourceFolder;
                if(targetFolder == null)
                {
                    targetFolder = sourceFolder;
                    txtTargetFolder.Text = targetFolder;
                }
                filesInSourceFolder = Directory.GetFiles(fbd.SelectedPath,"*.raw");
                currentImagNumber.Text = "1";
                totalImageCount.Text = (Math.Floor(filesInSourceFolder.Count()/3d)).ToString();
                if(filesInSourceFolder.Count() % 3 != 0)
                {
                    MessageBox.Show("Warning: The count of .raw files in the folder is not a multiple of 3. Files may be discarded.");
                }
                slide_currentFile.Maximum = (Math.Floor(filesInSourceFolder.Count() / 3d));
                slide_currentFile.Minimum = 1;
                slide_currentFile.Value = 1;
                btnProcessFolder.IsEnabled = true;
            }
        }

        private void Slide_currentFile_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {

            ReDrawPreview();
        }
        /*
        private byte[,] getBayerPattern()
        {
            return this.Dispatcher.Invoke(() =>
            {
                //0=Red, 1=Green,   2=Blue
                byte bayerColorA = (byte)int.Parse(colorBayerA.Text);
                byte bayerColorB = (byte)int.Parse(colorBayerB.Text);
                byte bayerColorC = (byte)int.Parse(colorBayerC.Text);
                byte bayerColorD = (byte)int.Parse(colorBayerD.Text);
                byte[,] bayerPattern = { { bayerColorA, bayerColorB }, { bayerColorC, bayerColorD } };
                return bayerPattern;
            });
        }*/

        // Order of colors
        private byte[] getColorOrder()
        {
            return this.Dispatcher.Invoke(() =>
            {
                //0=Red, 1=Green,   2=Blue
                byte colorAval = (byte)int.Parse(colorA.Text);
                byte colorBval = (byte)int.Parse(colorB.Text);
                byte colorCval = (byte)int.Parse(colorC.Text);
                byte[] colorOrder = { colorAval, colorBval, colorCval };
                return colorOrder;
            });
        }
        
        // Similar purpose as getColorOrder,
        // but the returned array's index is the 3 colors (0 red 1 green 2 blue) and the content is the position
        private byte[] getRGBPositions()
        {
            byte[] colorOrder = getColorOrder();
            byte[] RGBPositions = new byte[3];
            for(byte i =0;i<3;i++)
            {
                switch (colorOrder[i])
                {
                    case 0:
                        RGBPositions[0] = i;
                        break;
                    case 1:
                        RGBPositions[1] = i;
                        break;
                    case 2:
                        RGBPositions[2] = i;
                        break;
                    default:
                        // User entered some garbage!
                        break;
                }
            }
            return RGBPositions;
        }

        private void ReDrawPreview()
        {
            if(sourceFolder == null || filesInSourceFolder == null)
            {
                return; // Nothing to do here
            }

            int width = int.Parse(rawWidth.Text);
            int height = int.Parse(rawHeight.Text);
           

            //bool doPreviewDebayer = (bool)previewDebayer.IsChecked;
            bool doPreviewGamma = (bool)previewGamma.IsChecked;

            int frameDelay = int.Parse(delay.Text);
            byte[] RGBPositions = getRGBPositions();

            int sliderNumber = (int)slide_currentFile.Value;

            int baseIndex = (sliderNumber - 1)*3+ frameDelay;

            if((baseIndex + 2) > (filesInSourceFolder.Length -1))
            {
                MessageBox.Show("Triplet incomplete.");
                return;
            }

            int[] RGBIndizi = new int[3] { baseIndex+RGBPositions[0], baseIndex + RGBPositions[1], baseIndex + RGBPositions[2] };

            string[] RGBFiles = new string[3] { filesInSourceFolder[RGBIndizi[0]], filesInSourceFolder[RGBIndizi[1]] , filesInSourceFolder[RGBIndizi[2]] };

            //string selectedRawFile = filesInSourceFolder[index];
            /*if (!File.Exists(RGBFiles[0]) || !File.Exists(RGBFiles[1]) || !File.Exists(RGBFiles[2]))
            {
                MessageBox.Show("weirdo error, apparently file " + selectedRawFile + " (no longer?) exists");
                return;
            }
            else*/
            foreach(string file in RGBFiles)
            {
                if (!File.Exists(file))
                {
                    MessageBox.Show("weirdo error, apparently file " + file + " (no longer?) exists");
                    return;
                }
            }
            {
                int subsample = 4;

                int newWidth = (int)Math.Ceiling((double)width / subsample);
                int newHeight = (int)Math.Ceiling((double)height / subsample);

                byte[] buffR = File.ReadAllBytes(RGBFiles[0]);
                byte[] buffG = File.ReadAllBytes(RGBFiles[1]);
                byte[] buffB = File.ReadAllBytes(RGBFiles[2]);
                int byteDepth = 2; // This is for the source
                int byteWidth = newWidth * 3; // This is for the preview. 3 means RGB
                int newStride = Helpers.getStride(byteWidth);
                //byte[] newbytes = Helpers.PadLines(buff, height, width, newStride,2);

                byte[] newbytes;

                newbytes = Helpers.DrawPreview(buffR,buffG,buffB, newHeight, newWidth, height, width, newStride, byteDepth, subsample, doPreviewGamma);


                // Put preview into WPF image tag
                Bitmap manipulatedImage = new Bitmap(newWidth, newHeight, Imaging.PixelFormat.Format24bppRgb);
                Imaging.BitmapData pixelData = manipulatedImage.LockBits(new Rectangle(0, 0, newWidth, newHeight), Imaging.ImageLockMode.WriteOnly, Imaging.PixelFormat.Format24bppRgb);

                //Bitmap im = new Bitmap(width, height, newStride, Imaging.PixelFormat.Format16bppGrayScale,  Marshal.UnsafeAddrOfPinnedArrayElement(newbytes, 0));

                System.Runtime.InteropServices.Marshal.Copy(newbytes, 0, pixelData.Scan0, newbytes.Count());
                //im.GetPixel(1, 1);
                //im.GetPixel(2447, 2047);
                //pixelData.
                manipulatedImage.UnlockBits(pixelData);

                System.Runtime.InteropServices.Marshal.Copy(newbytes, 0, pixelData.Scan0, newbytes.Count());
                // Do the displaying
                mainPreview.Source = Helpers.BitmapToImageSource(manipulatedImage);
            }
        }

        private void PreviewGamma_Click(object sender, RoutedEventArgs e)
        {

            ReDrawPreview();
        }
        /*
        private void PreviewDebayer_Click(object sender, RoutedEventArgs e)
        {

            ReDrawPreview();
        }*/

        private void Color_TextChanged(object sender, TextChangedEventArgs e)
        {
            ReDrawPreview();
        }

        private void BtnLoadTargetFolder_Click(object sender, RoutedEventArgs e)
        {
            var fbd = new Ookii.Dialogs.Wpf.VistaFolderBrowserDialog();
            bool? result = fbd.ShowDialog();

            if (result == true && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
            {
                targetFolder = fbd.SelectedPath;
                txtTargetFolder.Text = targetFolder;
                txtStatus.Text = "Target fodler set to " + targetFolder;
            }
        }

        private void BtnProcessFolder_Click(object sender, RoutedEventArgs e)
        {
            worker.WorkerReportsProgress = true;
            worker.WorkerSupportsCancellation = true;
            worker.DoWork += worker_DoWork;
            worker.ProgressChanged += worker_ProgressChanged;
            worker.RunWorkerAsync();
        }

        public int CurrentProgress
        {
            get { return currentProgress; }
            private set
            {
                if (currentProgress != value)
                {
                    currentProgress = value;
                    OnPropertyChanged("CurrentProgress");                   
                }
            }
        }

        public string CurrentStatus
        {
            get { return currentStatus; }
            private set
            {
                if (currentStatus != value)
                {
                    currentStatus = value;
                    OnPropertyChanged("CurrentStatus");
                }
            }
        }

        private void worker_DoWork(object sender, DoWorkEventArgs e)
        {
            //byte[,] bayerPattern = getBayerPattern();

            _totalFiles = filesInSourceFolder.Length;
            _totalFiles = (int)Math.Floor(filesInSourceFolder.Length/3d);

            List<string[]> completeTriplets = new List<string[]>();

            int frameDelay = 0;
            byte[] RGBPositions = new byte[1];
            this.Dispatcher.Invoke(() =>
            {
                frameDelay = int.Parse(delay.Text);
                RGBPositions = getRGBPositions();
            });


            for(int baseIndex = frameDelay; baseIndex < filesInSourceFolder.Length; baseIndex += 3)
            {

                if ((baseIndex + 2) > (filesInSourceFolder.Length - 1))
                {
                    MessageBox.Show("Triplet incomplete. Skipping.");
                    continue;
                }

                int[] RGBIndizi = new int[3] { baseIndex + RGBPositions[0], baseIndex + RGBPositions[1], baseIndex + RGBPositions[2] };

                string[] RGBFiles = new string[3] { filesInSourceFolder[RGBIndizi[0]], filesInSourceFolder[RGBIndizi[1]], filesInSourceFolder[RGBIndizi[2]] };

                completeTriplets.Add(RGBFiles);
            }

            var countLock = new object();
            CurrentProgress = 0;

            Parallel.ForEach(completeTriplets,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, (currentTriplet, loopState) =>
                    // foreach (string srcFileName in filesInSourceFolder)
                {
                    if (worker.CancellationPending == true)
                    {
                        e.Cancel = true;
                        return;
                    }



                    string fileNameWithoutExtension =
                        targetFolder + "\\" + Path.GetFileNameWithoutExtension(currentTriplet[0]);
                    string fileName = fileNameWithoutExtension + ".tif";

                    _counter++;
                    var percentage = (double)_counter / _totalFiles * 100.0;
                    lock (countLock) { worker?.ReportProgress((int)percentage); }

                    if (File.Exists(fileName))
                    {
                        // Error: File already exists. No overwriting. Move on.
                        //continue;
                        return;
                    }

                    ProcessRAW(currentTriplet, fileName);
                });

            txtStatus.Text = "Finished";
        }

        private void worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            // pbStatus.Value = e.ProgressPercentage;
            CurrentProgress = e.ProgressPercentage;
            txtStatus.Text = $"Processed {_counter} out of {_totalFiles}";
           
            //this.Dispatcher.BeginInvoke(new Action(() => { pbStatus.Value = e.ProgressPercentage; }));
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            worker.CancelAsync();
        }

        private void MagnifierPosition_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ReDrawPreview();
        }

        private void Amplify_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ReDrawPreview();
        }

        private void Delay_TextChanged(object sender, TextChangedEventArgs e)
        {
            ReDrawPreview();
        }
    }
}
