/*
    NAPS2 (Not Another PDF Scanner 2)
    http://sourceforge.net/projects/naps2/
    
    Copyright (C) 2009       Pavel Sorejs
    Copyright (C) 2012       Michael Adams
    Copyright (C) 2013       Peter De Leeuw
    Copyright (C) 2012-2013  Ben Olden-Cooligan

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using NAPS2.Config;
using NAPS2.ImportExport;
using NAPS2.ImportExport.Email;
using NAPS2.ImportExport.Images;
using NAPS2.Lang;
using NAPS2.Lang.Resources;
using NAPS2.Recovery;
using NAPS2.Scan;
using NAPS2.Scan.Images;
using NAPS2.Update;

namespace NAPS2.WinForms
{
    public partial class FDesktop : FormBase, IScanReceiver, IAutoUpdaterClient
    {
        private readonly IEmailer emailer;
        private readonly IScannedImageImporter scannedImageImporter;
        private readonly ImageSaver imageSaver;
        private readonly StringWrapper stringWrapper;
        private readonly UserConfigManager userConfigManager;
        private readonly AppConfigManager appConfigManager;
        private readonly IErrorOutput errorOutput;
        private readonly IScannedImageFactory scannedImageFactory;
        private readonly RecoveryManager recoveryManager;
        private readonly ScannedImageList imageList = new ScannedImageList();
        private readonly AutoUpdaterUI autoUpdaterUI;

        public FDesktop(IEmailer emailer, ImageSaver imageSaver, StringWrapper stringWrapper, UserConfigManager userConfigManager, AppConfigManager appConfigManager, IErrorOutput errorOutput, IScannedImageFactory scannedImageFactory, RecoveryManager recoveryManager, IScannedImageImporter scannedImageImporter, AutoUpdaterUI autoUpdaterUI)
        {
            this.emailer = emailer;
            this.imageSaver = imageSaver;
            this.stringWrapper = stringWrapper;
            this.userConfigManager = userConfigManager;
            this.appConfigManager = appConfigManager;
            this.errorOutput = errorOutput;
            this.scannedImageFactory = scannedImageFactory;
            this.recoveryManager = recoveryManager;
            this.scannedImageImporter = scannedImageImporter;
            this.autoUpdaterUI = autoUpdaterUI;
            InitializeComponent();
        }

        private void FDesktop_Load(object sender, EventArgs e)
        {
            PostInitializeComponent();
        }

        private void PostInitializeComponent()
        {
            RelayoutToolbar();
            InitLanguageDropdown();
        }

        private void InitLanguageDropdown()
        {
            // Read a list of languages from the Languages.resx file
            var resourceManager = LanguageResources.ResourceManager;
            var resourceSet = resourceManager.GetResourceSet(CultureInfo.CurrentUICulture, true, true);
            foreach (DictionaryEntry entry in resourceSet.Cast<DictionaryEntry>().OrderBy(x => x.Value))
            {
                var langCode = ((string)entry.Key).Replace("_", "-");
                var langName = (string)entry.Value;

                // Only include those languages for which localized resources exist
                string localizedResourcesPath =
                    Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", langCode,
                        "NAPS2.Core.resources.dll");
                if (langCode == "en" || File.Exists(localizedResourcesPath))
                {
                    var button = new ToolStripMenuItem(langName, null, (sender, args) => SetCulture(langCode));
                    toolStripDropDownButton1.DropDownItems.Add(button);
                }
            }
        }

        private void RelayoutToolbar()
        {
            // Wrap text as necessary
            foreach (var btn in tStrip.Items.OfType<ToolStripItem>())
            {
                btn.Text = stringWrapper.Wrap(btn.Text, 80, CreateGraphics(), btn.Font);
            }
            // Reset padding
            SetToolbarButtonPadding(new Padding(10, 0, 10, 0));
            // Recalculate visibility for the below check
            Application.DoEvents();
            // Check if toolbar buttons are overflowing
            if (tStrip.Items.OfType<ToolStripItem>().Any(btn => !btn.Visible))
            {
                // Shrink the padding to help the buttons fit
                SetToolbarButtonPadding(new Padding(5, 0, 5, 0));
            }
        }

        private void SetToolbarButtonPadding(Padding padding)
        {
            foreach (var btn in tStrip.Items.OfType<ToolStripItem>())
            {
                btn.Padding = padding;
            }
        }

        private IEnumerable<int> SelectedIndices
        {
            get
            {
                return thumbnailList1.SelectedIndices.Cast<int>();
            }
            set
            {
                thumbnailList1.SelectedIndices.Clear();
                foreach (int i in value)
                {
                    thumbnailList1.SelectedIndices.Add(i);
                }
            }
        }

        public void ReceiveScannedImage(IScannedImage scannedImage)
        {
            imageList.Images.Add(scannedImage);
            AppendThumbnail(scannedImage);
        }

        private void UpdateThumbnails()
        {
            thumbnailList1.UpdateImages(imageList.Images);
            UpdateImageCounts();
        }

        private void AppendThumbnail(IScannedImage scannedImage)
        {
            thumbnailList1.AppendImage(scannedImage);
            UpdateImageCounts();
        }

        private void UpdateThumbnails(IEnumerable<int> selection)
        {
            UpdateThumbnails();
            SelectedIndices = selection;
        }

        private void UpdateImageCounts()
        {
            tsSavePDFAll.Text = tsSaveImagesAll.Text = tsEmailPDFAll.Text =
                string.Format(MiscResources.AllCount, imageList.Images.Count);
            tsSavePDFAll.Enabled = tsSaveImagesAll.Enabled = tsEmailPDFAll.Enabled = imageList.Images.Any();

            tsSavePDFSelected.Text = tsSaveImagesSelected.Text = tsEmailPDFSelected.Text =
                string.Format(MiscResources.SelectedCount, SelectedIndices.Count());
            tsSavePDFSelected.Enabled = tsSaveImagesSelected.Enabled = tsEmailPDFSelected.Enabled = SelectedIndices.Any();
        }

        private void Clear()
        {
            if (imageList.Images.Count > 0)
            {
                if (MessageBox.Show(string.Format(MiscResources.ConfirmClearItems, imageList.Images.Count), MiscResources.Clear, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                {
                    imageList.Delete(Enumerable.Range(0, imageList.Images.Count));
                    UpdateThumbnails();
                }
            }
        }

        private void Delete()
        {
            if (SelectedIndices.Any())
            {
                if (MessageBox.Show(string.Format(MiscResources.ConfirmDeleteItems, SelectedIndices.Count()), MiscResources.Delete, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                {
                    imageList.Delete(SelectedIndices);
                    UpdateThumbnails();
                }
            }
        }

        private void SelectAll()
        {
            SelectedIndices = Enumerable.Range(0, imageList.Images.Count);
        }

        private void MoveDown()
        {
            UpdateThumbnails(imageList.MoveDown(SelectedIndices));
        }

        private void MoveUp()
        {
            UpdateThumbnails(imageList.MoveUp(SelectedIndices));
        }

        private void RotateLeft()
        {
            UpdateThumbnails(imageList.RotateFlip(SelectedIndices, RotateFlipType.Rotate270FlipNone));
        }

        private void RotateRight()
        {
            UpdateThumbnails(imageList.RotateFlip(SelectedIndices, RotateFlipType.Rotate90FlipNone));
        }

        private void Flip()
        {
            UpdateThumbnails(imageList.RotateFlip(SelectedIndices, RotateFlipType.RotateNoneFlipXY));
        }

        private void ExportPDF(string filename, List<IScannedImage> images)
        {
            var pdfdialog = FormFactory.Create<FPDFSave>();
            pdfdialog.Filename = filename;
            pdfdialog.Images = images;
            pdfdialog.ShowDialog(this);
        }

        private void thumbnailList1_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Delete:
                    Delete();
                    break;
                case Keys.Left:
                    if (e.Control)
                    {
                        MoveUp();
                    }
                    break;
                case Keys.Right:
                    if (e.Control)
                    {
                        MoveDown();
                    }
                    break;
                case Keys.A:
                    if (e.Control)
                    {
                        SelectAll();
                    }
                    break;
            }
        }

        private void thumbnailList1_ItemActivate(object sender, EventArgs e)
        {
            if (SelectedIndices.Any())
            {
                using (var image = imageList.Images[SelectedIndices.First()].GetImage())
                {
                    var viewer = FormFactory.Create<FViewer>();
                    viewer.Image = image;
                    viewer.ShowDialog();
                }
            }
        }

        private void tsScan_Click(object sender, EventArgs e)
        {
            var prof = FormFactory.Create<FChooseProfile>();
            prof.ScanReceiver = this;
            prof.ShowDialog();
        }

        private void tsSavePDFAll_Click(object sender, EventArgs e)
        {
            SavePDF(imageList.Images);
        }

        private void tsSavePDFSelected_Click(object sender, EventArgs e)
        {
            SavePDF(imageList.Images.ElementsAt(SelectedIndices).ToList());
        }

        private void SavePDF(List<IScannedImage> images)
        {
            if (images.Any())
            {
                var sd = new SaveFileDialog
                {
                    OverwritePrompt = true,
                    AddExtension = true,
                    Filter = MiscResources.FileTypePdf + "|*.pdf"
                };

                if (sd.ShowDialog() == DialogResult.OK)
                {
                    ExportPDF(sd.FileName, images);
                }
            }
        }

        private void tsSaveImagesAll_Click(object sender, EventArgs e)
        {
            SaveImages(imageList.Images);
        }

        private void tsSaveImagesSelected_Click(object sender, EventArgs e)
        {
            SaveImages(imageList.Images.ElementsAt(SelectedIndices).ToList());
        }

        private void SaveImages(List<IScannedImage> images)
        {
            if (images.Any())
            {
                var sd = new SaveFileDialog
                    {
                        OverwritePrompt = true,
                        AddExtension = true,
                        Filter = MiscResources.FileTypeBmp + "|*.bmp|" +
                                 MiscResources.FileTypeEmf + "|*.emf|" +
                                 MiscResources.FileTypeExif + "|*.exif|" +
                                 MiscResources.FileTypeGif + "|*.gif|" +
                                 MiscResources.FileTypeJpeg + "|*.jpg;*.jpeg|" +
                                 MiscResources.FileTypePng + "|*.png|" +
                                 MiscResources.FileTypeTiff + "|*.tiff;*.tif",
                        DefaultExt = "jpg",
                        FilterIndex = 5
                    };

                if (sd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        imageSaver.SaveImages(sd.FileName, images, path =>
                        {
                            if (images.Count() == 1)
                            {
                                // One image, so the file name is the same and the save dialog already prompted the user to overwrite
                                return true;
                            }
                            switch (
                                MessageBox.Show(
                                    string.Format(MiscResources.ConfirmOverwriteFile, path),
                                    MiscResources.OverwriteFile, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning))
                            {
                                case DialogResult.Yes:
                                    return true;
                                case DialogResult.No:
                                    return false;
                                default:
                                    throw new InvalidOperationException("User cancelled");
                            }
                        });
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }
        }

        private void tsEmailPDFAll_Click(object sender, EventArgs e)
        {
            EmailPDF(imageList.Images);
        }

        private void tsEmailPDFSelected_Click(object sender, EventArgs e)
        {
            EmailPDF(imageList.Images.ElementsAt(SelectedIndices).ToList());
        }

        private void EmailPDF(List<IScannedImage> images)
        {
            if (images.Any())
            {
                string path = Paths.AppData + "\\Scan.pdf";
                ExportPDF(path, images);
                emailer.SendEmail(new EmailMessage
                {
                    Attachments = new List<EmailAttachment> { new EmailAttachment
                        {
                            FilePath = path,
                            AttachmentName = Path.GetFileName(path)
                        } },
                });
                File.Delete(path);
            }
        }

        private void tsFlip_Click(object sender, EventArgs e)
        {
            Flip();
        }

        private void tsClear_Click(object sender, EventArgs e)
        {
            Clear();
        }

        private void tsDelete_Click(object sender, EventArgs e)
        {
            Delete();
        }

        private void tsMoveUp_Click(object sender, EventArgs e)
        {
            MoveUp();
        }

        private void tsMoveDown_Click(object sender, EventArgs e)
        {
            MoveDown();
        }

        private void tsRotateLeft_Click(object sender, EventArgs e)
        {
            RotateLeft();
        }

        private void tsRotateRight_Click(object sender, EventArgs e)
        {
            RotateRight();
        }

        private void tsProfiles_Click(object sender, EventArgs e)
        {
            FormFactory.Create<FManageProfiles>().ShowDialog();
        }

        private void tsAbout_Click(object sender, EventArgs e)
        {
            FormFactory.Create<FAbout>().ShowDialog();
        }

        private void SetCulture(string cultureId)
        {
            userConfigManager.Config.Culture = cultureId;
            userConfigManager.Save();
            Thread.CurrentThread.CurrentCulture = new CultureInfo(cultureId);
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(cultureId);

            // Update localized values
            // Since all forms are opened modally and this is the root form, it should be the only one that needs to be updated live
            SaveFormState = false;
            Controls.RemoveAll();
            UpdateRTL();
            InitializeComponent();
            PostInitializeComponent();
            UpdateThumbnails();
            Focus();
            WindowState = FormWindowState.Normal;
            DoRestoreFormState();
            SaveFormState = true;
        }

        private void FDesktop_Shown(object sender, EventArgs e)
        {
            // If configured (e.g. by a business), show a customizable message box on application startup.
            var appConfig = appConfigManager.Config;
            if (!string.IsNullOrWhiteSpace(appConfig.StartupMessageText))
            {
                MessageBox.Show(appConfig.StartupMessageText, appConfig.StartupMessageTitle, MessageBoxButtons.OK,
                    appConfig.StartupMessageIcon);
            }

            // Allow scanned images to be recovered in case of an unexpected close
            foreach (IScannedImage scannedImage in recoveryManager.RecoverScannedImages())
            {
                imageList.Images.Add(scannedImage);
            }
            UpdateThumbnails();

            // Automatic updates
            // Not yet enabled
            // autoUpdaterUI.OnApplicationStart(this);
        }

        private void FDesktop_Closed(object sender, EventArgs e)
        {
            // TODO: Add a closing confirmation
            imageList.Delete(Enumerable.Range(0, imageList.Images.Count));
        }

        private void tsImport_Click(object sender, EventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Multiselect = true,
                CheckFileExists = true
            };
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                foreach (var fileName in ofd.FileNames)
                {
                    // TODO: Run in thread, and show a dialog (just like exporting)
                    // Need to provide count somehow (progress callback). count = # files or # pages
                    var images = scannedImageImporter.Import(fileName);
                    foreach (var img in images)
                    {
                        imageList.Images.Add(img);
                        AppendThumbnail(img);
                        thumbnailList1.Refresh();
                    }
                }
            }
        }

        public void UpdateAvailable(VersionInfo versionInfo)
        {
            Invoke(new Action(() => autoUpdaterUI.PerformUpdate(this, versionInfo)));
        }

        public void InstallComplete()
        {
            Invoke(new Action(() =>
            {
                switch (MessageBox.Show(MiscResources.InstallCompletePromptRestart, MiscResources.InstallComplete, MessageBoxButtons.YesNo, MessageBoxIcon.Question))
                {
                    case DialogResult.Yes:
                        Close(); // TODO: This close might be canceled. Handle that.
                        Process.Start(Application.ExecutablePath);
                        break;
                }
            }));
        }

        private void thumbnailList1_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateImageCounts();
        }

        private void tsInterleave_Click(object sender, EventArgs e)
        {
            UpdateThumbnails(imageList.Interleave(SelectedIndices));
        }

        private void tsDeinterleave_Click(object sender, EventArgs e)
        {
            UpdateThumbnails(imageList.Deinterleave(SelectedIndices));
        }

        private void thumbnailList1_MouseMove(object sender, MouseEventArgs e)
        {
            Cursor = thumbnailList1.GetItemAt(e.X, e.Y) == null ? Cursors.Default : Cursors.Hand;
        }

        private void thumbnailList1_MouseLeave(object sender, EventArgs e)
        {
            Cursor = Cursors.Default;
        }

        private void tsOcr_Click(object sender, EventArgs e)
        {
            // TODO: Conditional
            FormFactory.Create<FOcrLanguages>().ShowDialog();
        }
    }
}
