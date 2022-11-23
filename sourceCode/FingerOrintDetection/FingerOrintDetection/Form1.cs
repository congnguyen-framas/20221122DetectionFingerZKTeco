using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
//using ImageProcessor.Imaging.Formats;
using libzkfpcsharp;


namespace FingerOrintDetection
{
    public partial class Form1 : Form
    {
        IntPtr mDevHandle = IntPtr.Zero;
        IntPtr mDBHandle = IntPtr.Zero;
        IntPtr FormHandle = IntPtr.Zero;
        bool bIsTimeToDie = false;
        bool IsRegister = false;
        bool bIdentify = true;
        byte[] FPBuffer;//chứa mã code base64 của fingerrint
        byte[] FPBufferTest;//chứa mã code base64 của fingerrint

        int RegisterCount = 0;
        const int REGISTER_FINGER_COUNT = 3;

        byte[][] RegTmps = new byte[3][];
        byte[] RegTmp = new byte[2048];
        byte[] CapTmp = new byte[2048];
        int cbCapTmp = 2048;
        int cbRegTmp = 0;
        int iFid = 1;//define Id cho fingerprint. băt đầu từ 1
        Thread captureThread = null;

        private int mfpWidth = 0;
        private int mfpHeight = 0;

        const int MESSAGE_CAPTURED_OK = 0x0400 + 6;

        [DllImport("user32.dll", EntryPoint = "SendMessageA")]
        public static extern int SendMessage(IntPtr hwnd, int wMsg, IntPtr wParam, IntPtr lParam);

        public Form1()
        {
            InitializeComponent();

            Load += Form1_Load;
        }

        /// <summary>
        /// Method Reading fingerprint.
        /// </summary>
        private void DoCapture()
        {
            while (!bIsTimeToDie)
            {
                cbCapTmp = 2048;
                int ret = zkfp2.AcquireFingerprint(mDevHandle, FPBuffer, CapTmp, ref cbCapTmp);
                if (ret == zkfp.ZKFP_ERR_OK)
                {
                    SendMessage(FormHandle, MESSAGE_CAPTURED_OK, IntPtr.Zero, IntPtr.Zero);
                }
                Thread.Sleep(200);
            }
        }

        protected override void DefWndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case MESSAGE_CAPTURED_OK:
                    {
                        MemoryStream ms = new MemoryStream();
                        BitmapFormat.GetBitmap(FPBuffer, mfpWidth, mfpHeight, ref ms);
                        Bitmap bmp = new Bitmap(ms);
                        this.picFPImg.Image = bmp;
                        //Show capture fingerprint encrypt base64
                        txtFingerprintCode.Text = $"Fingerprint:{Environment.NewLine}{zkfp2.BlobToBase64(CapTmp, cbCapTmp)}";

                        if (IsRegister)
                        {
                            int ret = zkfp.ZKFP_ERR_OK;
                            int fid = 0, score = 0;
                            ret = zkfp2.DBIdentify(mDBHandle, CapTmp, ref fid, ref score);
                            if (zkfp.ZKFP_ERR_OK == ret)
                            {
                                textRes.Text = "This finger was already register by " + fid + "!";
                                return;
                            }
                            if (RegisterCount > 0 && zkfp2.DBMatch(mDBHandle, CapTmp, RegTmps[RegisterCount - 1]) <= 0)
                            {
                                textRes.Text = "Please press the same finger 3 times for the enrollment";
                                return;
                            }
                            Array.Copy(CapTmp, RegTmps[RegisterCount], cbCapTmp);
                            String strBase64 = zkfp2.BlobToBase64(CapTmp, cbCapTmp);
                            byte[] blob = zkfp2.Base64ToBlob(strBase64);
                            RegisterCount++;

                            if (RegisterCount >= REGISTER_FINGER_COUNT)
                            {
                                RegisterCount = 0;
                                if (zkfp.ZKFP_ERR_OK == (ret = zkfp2.DBMerge(mDBHandle, RegTmps[0], RegTmps[1], RegTmps[2], RegTmp, ref cbRegTmp)) &&
                                       zkfp.ZKFP_ERR_OK == (ret = zkfp2.DBAdd(mDBHandle, iFid, RegTmp)))
                                {
                                    iFid++;
                                    textRes.Text = $"enroll succ";
                                    txtFingerprintCode.Text = $"Fingerprint:{Environment.NewLine}{zkfp2.BlobToBase64(RegTmp, cbRegTmp)}";
                                }
                                else
                                {
                                    textRes.Text = "enroll fail, error code=" + ret;
                                }
                                IsRegister = false;
                                return;
                            }
                            else
                            {
                                textRes.Text = "You need to press the " + (REGISTER_FINGER_COUNT - RegisterCount) + " times fingerprint";
                            }
                        }
                        else
                        {
                            if (cbRegTmp <= 0)
                            {
                                textRes.Text = "Please register your finger first!";
                                return;
                            }

                            if (bIdentify)//Xác thực vân tay
                            {
                                int ret = zkfp.ZKFP_ERR_OK;
                                int fid = 0, score = 0;
                                ret = zkfp2.DBIdentify(mDBHandle, CapTmp, ref fid, ref score);
                                if (zkfp.ZKFP_ERR_OK == ret)
                                {
                                    textRes.Text = "Identify succ, fid= " + fid + ",score=" + score + "!";
                                    return;
                                }
                                else
                                {
                                    textRes.Text = "Identify fail, ret= " + ret;
                                    return;
                                }
                            }
                            else//Kiểm chứng lại vân tay(verify)
                            {
                                int ret = zkfp2.DBMatch(mDBHandle, CapTmp, RegTmp);
                                if (0 < ret)
                                {
                                    textRes.Text = "Match finger succ, score=" + ret + "!";
                                    return;
                                }
                                else
                                {
                                    textRes.Text = "Match finger fail, ret= " + ret;
                                    return;
                                }
                            }
                        }
                    }
                    break;

                default:
                    base.DefWndProc(ref m);
                    break;
            }
        }

        private void CloseDevice()
        {
            if (IntPtr.Zero != mDevHandle)
            {
                bIsTimeToDie = true;
                Thread.Sleep(1000);
                captureThread.Join();
                zkfp2.CloseDevice(mDevHandle);
                mDevHandle = IntPtr.Zero;
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            FormHandle = this.Handle;
        }

        private void bnIdentify_Click(object sender, EventArgs e)
        {
            IsRegister = false;
            cbRegTmp = cbCapTmp;

            if (!bIdentify)
            {
                bIdentify = true;
                textRes.Text = "Please press your finger!";
            }
        }

        private void bnClose_Click(object sender, EventArgs e)
        {
            CloseDevice();
            RegisterCount = 0;
            Thread.Sleep(1000);
            bnInit.Enabled = false;
            bnFree.Enabled = true;
            bnOpen.Enabled = true;
            bnClose.Enabled = false;
            bnEnroll.Enabled = false;
            bnVerify.Enabled = false;
            bnIdentify.Enabled = false;
        }

        private void bnVerify_Click(object sender, EventArgs e)
        {
            IsRegister = false;
            cbRegTmp = cbCapTmp;

            if (bIdentify)
            {
                bIdentify = false;
                textRes.Text = "Please press your finger!";
            }
        }

        private void bnEnroll_Click(object sender, EventArgs e)
        {
            if (!IsRegister)
            {
                IsRegister = true;
                RegisterCount = 0;
                cbRegTmp = 0;
                textRes.Text = "Please press your finger 3 times!";
            }
        }



        private void bnInit_Click(object sender, EventArgs e)
        {
            cmbIdx.Items.Clear();
            int ret = zkfperrdef.ZKFP_ERR_OK;
            if ((ret = zkfp2.Init()) == zkfperrdef.ZKFP_ERR_OK)
            {
                int nCount = zkfp2.GetDeviceCount();
                if (nCount > 0)
                {
                    for (int i = 0; i < nCount; i++)
                    {
                        cmbIdx.Items.Add(i.ToString());
                    }
                    cmbIdx.SelectedIndex = 0;
                    bnInit.Enabled = false;
                    bnFree.Enabled = true;
                    bnOpen.Enabled = true;
                }
                else
                {
                    zkfp2.Terminate();
                    MessageBox.Show("No device connected!");
                }
            }
            else
            {
                MessageBox.Show("Initialize fail, ret=" + ret + " !");
            }
        }

        private void bnFree_Click(object sender, EventArgs e)
        {
            zkfp2.Terminate();
            cbRegTmp = 0;
            bnInit.Enabled = true;
            bnFree.Enabled = false;
            bnOpen.Enabled = false;
            bnClose.Enabled = false;
            bnEnroll.Enabled = false;
            bnVerify.Enabled = false;
            bnIdentify.Enabled = false;
        }

        private void bnOpen_Click(object sender, EventArgs e)
        {
            int ret = zkfp.ZKFP_ERR_OK;
            if (IntPtr.Zero == (mDevHandle = zkfp2.OpenDevice(cmbIdx.SelectedIndex)))
            {
                MessageBox.Show("OpenDevice fail");
                return;
            }
            if (IntPtr.Zero == (mDBHandle = zkfp2.DBInit()))
            {
                MessageBox.Show("Init DB fail");
                zkfp2.CloseDevice(mDevHandle);
                mDevHandle = IntPtr.Zero;
                return;
            }
            bnInit.Enabled = false;
            bnFree.Enabled = true;
            bnOpen.Enabled = false;
            bnClose.Enabled = true;
            bnEnroll.Enabled = true;
            bnVerify.Enabled = true;
            bnIdentify.Enabled = true;
            btnClear.Enabled = true;
            btnAddFinger.Enabled = true;
            RegisterCount = 0;
            cbRegTmp = 0;
            iFid = 1;
            for (int i = 0; i < 3; i++)
            {
                RegTmps[i] = new byte[2048];
            }
            byte[] paramValue = new byte[4];
            int size = 4;
            zkfp2.GetParameters(mDevHandle, 1, paramValue, ref size);
            zkfp2.ByteArray2Int(paramValue, ref mfpWidth);

            size = 4;
            zkfp2.GetParameters(mDevHandle, 2, paramValue, ref size);
            zkfp2.ByteArray2Int(paramValue, ref mfpHeight);

            FPBuffer = new byte[mfpWidth * mfpHeight];
            FPBufferTest = new byte[mfpWidth * mfpHeight];

            captureThread = new Thread(new ThreadStart(DoCapture));
            captureThread.IsBackground = true;
            captureThread.Start();
            bIsTimeToDie = false;
            textRes.Text = "Open succ";
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            //for (int i = 0; i < iFid; i++)
            //{
            //    zkfp2.DBDel(mDBHandle, i);
            //}

            if(zkfp2.DBClear(mDBHandle) == zkfp.ZKFP_ERR_OK)
            {
                iFid = 1;
                textRes.Text = "Cleared all fingerprint!";
            }
        }

        /// <summary>
        /// dung de test.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnAddFinger_Click(object sender, EventArgs e)
        {
            //Cong
            FPBufferTest = zkfp2.Base64ToBlob("Sp9TUzIxAAAD3OEECAUHCc7QAAAj3WoBAABkgwEjYtzgAPxkkAB+AAm4OADZAGZkmAAX3QFkrADYAEtkjtyVAA" +
                            "lksgBkAIm4DgDkAOVkSQBM3VFSPQBzALVko9xaAVAzhwCXAPm4XwD9APpk+ADd3O9kfACtAMJkTNwOAVxkfgDkASy4xADcABlcZgAz3ThRXQBJARdhWdxv" +
                            "APlkEwD2AU+4awBYAPhkbwBU3AJPSQDuAKRkodzsAItkjwDIARy4jQCxAIVkuQCW3HtkJgCxACpkK9wnAU1kMwBAAPK4wAA2ATg6/QBZ3TBFpwBgAQAo8k" +
                            "PIBwoRKIfUBjXeQXpBByZu1KRC1pPRRQb5dlOG9ky4h9IEhoKjBl1fk4OX+G/6jAJJUA8aZYEJezp5S1LIAvYb/gRL+fDdyP7loiGb/Yq6UZf1hYQ5jlD+N" +
                            "oAAZt79LRMnMWEm7I7Wkh+TlwChNtsOHqK3hEqlYt8Ai/ILHQKSHiAv4AOCAPeJwwn809L/xXU5awD/yiJPbeZ2vfP/lY02LX+FgL1/wHBKqjP0wotfAHoB" +
                            "nt4f82seZYG/+9zY0wCjdKfp+2LwOgbmVHZWdL6MsGvm3QMgOAHHFRm0BgB8RW1KlwQDSEt3awUAo5J9ZtoBZFtxwFLOAMK7h/95Tl0KxV1tKz89XAMAO7d" +
                            "0wtgBPXdtPQ3FNIAx/j7/REwOxTaLKFL//v9dN8YALVV1wgcAkJLDN1nXAXmTfWnBBcP9pwIAkJgMwcIAeUV7am0RANlTkMOiwYFpWcASxduiTMFqwsH+ww" +
                            "XAfR2ABgCTsgaHwAjcirSDeMLBosAF3JO5Bv5bC8WKv1rDwXjCWQfFkr3QREsJAKvWVW+LHRAAwNmTagTBih1dwXAJAKgfifwdkGAGADfconjD2QGw3BbAN" +
                            "9YAAArd//1CRsD1wTsdEwDUuoxiBP/AtnWEwAsAYiX3/PT+OMAHADomZ8IdwGYHAE7sKP04IwgARu1nwb3Awx4KAGH59/47/y3hCABi/vr+O//9Iy8KEFoA" +
                            "awWXdhwEEIsKkKDQENTYn2nBwWyJOolnHQYQkwwX/vb+C8xND2SSgAjVkhL8/jAgCBBP1mCRHIwKEGET+jn9ICA/ChB4HIkBxcAfwMPEwQQQRSAjwRIQ1Rug" +
                            "wavCch7Dwm7C/8LCEH34LPz//DUF1WkkuMXEwQYQKe1QdB4EEHEsOiHBEG/vSDoEEKUz9S0HzBQ1SWAWEAglrh9ukMGdwMIH/8FVwRIQoVbDR5LHGMLDwJef" +
                            "DtVZa2zB/CT5+Pnm/VGeARJDAQEBxWwC7kwAABJFUg==");
            //zkfp2.DBMerge(mDBHandle, FPBufferTest, FPBufferTest, FPBufferTest, RegTmp, ref cbRegTmp);
            if (zkfp2.DBAdd(mDBHandle, iFid, FPBufferTest) == zkfp.ZKFP_ERR_OK)
            {
                iFid += 1;
            } 
            FPBufferTest = zkfp2.Base64ToBlob("Sr1TUzIxAAAD/v8ECAUHCc7QAAAj/2oBAABZgyMklP4BAfRkmQDVAVKadwDeAOhkHQA3/0hL8QAVAfI4ff69AGJkyABhAA" +
                "+aswCcAHhkYwCR/v5kVgBAAYldA/9RATI0/wCzAI7AyAD7ACRkaAAc/0lenAC8AD5kjP60APNkYwDEAVuarABGAcpYaQBR/99QtwBZAb9Q8f5IATY19wCXAUbMjQC" +
                "AAPVkBQBK/gFksAATAZJkov7HAG5kpwD9Acug3gDGAItRrgAd/01kggBBAfxMVv7nAOFkBwHeATLe/gA7ATUimABM/0lHYABdAfov1v5IAIBkyaRvnLVQ7Fw1/kkK1w" +
                "lhd++SVhRCFtMWVvSn8n4GEXHnaGmDBJYKDp6CYHgakGN+nYgaDFP2ZvprDA4CxO4k/A4Dlg9bg4+Pe824InbsBPPV+kSDt3EYCAaYofg4ka54XwzWeP5yPe9m+uRR" +
                "ZBe4Z5gbn2G0sba1dPOYBCr/NRboEwkSY/ikgsoFgYHjD153d+2wCsZkwHd0fp3z6ACBg/HvW4JElJYX6vSOApsXPYy78vLvzohT+YB92P6a/KP9bAGK+5jycQjp/O" +
                "8S+gb39CYLgYHm91h9+7u7iQsg8AEB1Rq6BgC7Sb/Bwj7AAwDbSQk6DQOKafRc/cD9B/3Czw4A/nOTg71+wzzBwQ8A/XlKwcEBgnzAdAMBwHoUAQ0AkID0wP3+/LX+" +
                "DwETd5cFwXw6/8PAwMFszwCqcftAQEYJAGSTd4p/wQsAqZTFwEMBR/8LALKauMJ3PGvACQC6nMbALABFCwCynnSiwcM8wWAIAMyjw/47Pv4GAMSlgE7DC/7NqAz/Rk" +
                "HXARpilsB4iHL+AWrD7QAYpZrCZgSLdYHAVg0AkrUxPP2+Rv8HAHu8osHCPm4GAJ28+vX/Bv5/wGJkBABax3J8DgDcyIn/B4PAgFvBBgDkydM+/OoAFOegccAEjMGH" +
                "clkEAMP6SakJ/sr6Gvz/Kp4IEy9nzsHBwcFdEQOZ/9xAwCr/O//9AToFEF8DWgf+w/sRYwZWwU3IEMn2lsPFwcTEQMGW6gAR+6TBwATDajzCfP/Cwv1RCRNgDO38+v" +
                "ojOxMTEQ6pwcORxAXEh3mBCBDNEDHmQQnulxJXksLDAcbF9hGbFFPDjA3FB+6xFV6pExHBFa48wYjDhpPCr8AH7vEXOk8DEDAXMgAFEGshUHjAENTLSD0XEREdaMHD" +
                "fomCd8JyU8kQnry2/h31+/w6/Pj9EbFC18QD1bIgvfwZEQ5KsJDCf2yWiID/wYPGEPO1R8AYEORnDG2BP8LDwsPDwgfDwj/D/8HCwlKHABq9AAIBALQB40cD/wGoAT7" +
                "IAMUZRqw=");
            //zkfp2.DBMerge(mDBHandle, FPBufferTest, FPBufferTest, FPBufferTest, RegTmp, ref cbRegTmp);
            if (zkfp2.DBAdd(mDBHandle, iFid, FPBufferTest) == zkfp.ZKFP_ERR_OK)
            {
                iFid += 1;
            }

            //dat
            FPBufferTest = zkfp2.Base64ToBlob("SxtTUzIxAAACWFsECAUHCc7QAAAaWWkBAAAAgoUKf1h7AAcOkQC4AIVWeACeAIEPkgCqWP4PwgC2AOYPbVjWAHoPMgAvAF" +
                "ZXyAD1ACsPpwD/WFoORwAgAfoPg9m/idcLgYGTCmHTd4XGfdr+s4Gt3V77m/sDnyqnVHq2fc/7P/maFyGHEgvns9fTM+KAR7YJHufz79LHRlghLQEBwBVqBQL5GBDAW" +
                "QcAaSgNHz8JAJ0wBjvA/WbBCwCVOAk6RlVvDQCDQQbAOv7CmEdCDQCES8Y1w25AwQQAQ2AoKwlYlVgJPv//BP4/XAFCaPRBCcWQYFTBwPzC/v0ECgLYawZA/0bAwQAw" +
                "2vE4CgBwdTj//gb+UwQAMog/wv1TAWx+A/7BOv5CDwUAMpPwOM4Abt8H/0Y3QgfFdZjbeHMRADKeLv45psHB/f/+/zpdCVh8nwn/Kf4EwEBdAWOgd28NxXCqWPz//URRR" +
                "MEAx+EhRgUAK8Ei/ytVAXCvBv7+OjXCpsBKBgAi1Rv+/WkNAGi3AyoF/UgcCQBq1n3DB8OGXgAG2T1M/8AAJYPhMw4AZL7G/cKkwf3B/sHAOn8TWDTn3v7+/j7+K6fB/sH" +
                "AbxTFGe6vxv7///39O///d01zBAAy7pZ9BljK+SlGCQCb/2CcnMGDCRBoxxf5HcBsCBBmCeH9ZCsGEFoNTMS0wgpIYA5GicLBBAcSPxc3aYMGEIMjQibDFhAwPcD4Mf+kw" +
                "fn+/v//BcDCmWsVEDtOt/D//Kb8/vz6/S+hbxZIRl20//8wOvz/o/j+/v/BwARl");
            //zkfp2.DBMerge(mDBHandle, FPBufferTest, FPBufferTest, FPBufferTest, RegTmp, ref cbRegTmp);
            if (zkfp2.DBAdd(mDBHandle, iFid, FPBufferTest) == zkfp.ZKFP_ERR_OK)
            {
                iFid += 1;
            }
            //Thi
            FPBufferTest = zkfp2.Base64ToBlob("S59TUzIxAAAC3OEECAUHCc7QAAAa3WkBAAAAggEVsNwZABkPYACBAPjThgBaAHwPegBZ3J0PWAB5AJcPv9x/AKMPmABHAIvTMwCNAE" +
                "sPjQCd3EQP0wCmAPwPgNy/ACUPygADAETTnADIAK0PYQDW3KIPrgDlAEwPfdztABcOvwA+AOPSYgD9ACIPUwAJ3Y8PgwAMAcMPgNwgAZ4Po38Gn9X/n4PPqqtz8/eYWwbiKvlH" +
                "FqIigNKfC/daogmfHQmw9+mqD9vulAsgJw5T2f8O9pYjlCj2mLOPaXuzhp3MBwyuppq/OAxjV/8lifgJHbOCozUcq6LdphAT9JBWRFsSV0tAjwxwwYKEcYlS8F96e6W8aTbwpZkHE" +
                "IWgf7AdICkBxL8a2QoASQf9ZTvBaR0GAKMMEMI5wf8jBwC1GRpHOhAC4hztNf88/olEEtw5LOkzSvwF/v0c/kMIAGRAMf7/7P4IAFxGaQf/wB5dEQBnRvfmPf8j/sDAwf091gAol" +
                "N87/sD+//X//Ig3FAArVOCDMTYd/P84ShAARlaBfMCMwsD+gwcQAhxWoHzDecIEb5rZAYtaEP8p0wCGgYGdw4xmfAXEwqjDCADCXykF/0YcFABcdef/OP0h7/4z/yAXEsVUe4t/w" +
                "nHAw4G2wxHcv3qrwsKLXpPDrH4PAFh9UENZgrLDEACWfpd1w8AfbXGFCQC/Ry39I//AwioPAFGDjh/Hw8TBwsIFwsIew8EHAJuG4jsr2gExkEzB/0oEAlqYJDEQAEdmRnodgsDCe" +
                "ZQExYah7IAHANSpOgXAwhz4JACDwisGwscbx8OLw4bDAMHHTn/BwnVwo8wAyBVB/f39/P7eBQJ81JcjCAB6Z0PBHMLDwcUMAETeq/L+/Pj3/DPKAKtgtozExsbFB8KKHAcAf/Ainw" +
                "EGAuz4KcDCWQXFz/m8/S8DACn76MEGzGUAInMHEAoFZSIo/QUQvx+o/S3c");
            //zkfp2.DBMerge(mDBHandle, FPBufferTest, FPBufferTest, FPBufferTest, RegTmp, ref cbRegTmp);
            if (zkfp2.DBAdd(mDBHandle, iFid, FPBufferTest) == zkfp.ZKFP_ERR_OK)
            {
                iFid += 1;
            }
            //Tuan
            FPBufferTest = zkfp2.Base64ToBlob("TQVTUzIxAAAERkYECAUHCc7QAAAcR2kBAAAAhOskmUYhAHgOjQDhAPlIygAtAIgPTAAxRgUOlQBHAL4PZ0ZLAP4OdACOAHhIswBOAAkPugBL" +
                "RvoOqwBSAEQPxkZfAA0PPwChAGVJhQBtAHEPHwBzRh0PmQCTAL0PEkaVAFsP5ABcACpJswCaACcPCwClRi8PhQCtAJQOXEazAEoPpgABAE9IwwDTAEsPJwDbRkAPLwDkAIcPx0byAFcP" +
                "2QA+AEpJMQD/AMIPhQAFRzgPcQAIAdIPjEYbAfwPGwDgAbdJPgA0Aa8PSgAwR44PpgA9ASsPPkZLAZ8PgXysd1a4eITN+LqFk4KDVlYIRQiliggLqTx4ghn+hYRMB2q5FYOJgOZ4JYqd" +
                "uAOQFIN5gpD77DyUChV3FQbMj5K3B2cP538LD3i6sOP7BhGa8XfzERVnKLYLA//+E19dxAIKFdMJILNhvaffRQLdC9sXDp5LCq4LMvtSCyORSwDO/gInDwByskcNjffO+W/tecVrCR/7" +
                "OQovDBZfwPoBD98EAIh5xRcTzXjy+YokgVny4JOJuW/k569W3wai8/MU2BJmuJJ5AaKpl155JSQ29DfbrfJC7KMtTNp0tQ0g/wEGNSCRBAB7BkVZA0aBDXpwWQzFmRrGd3Z6wgQAZCECe" +
                "w8AzSKQwgRfcxPBEgDHMImqZnCGh8DAwQcASDP5eDgMAIU0er3AxYaGwAgAjjnFQ/omBABxTnrDqQQExE8Awf4FAHJODbk+AwCvUw8HEgTuVIDAwIaABcL6OHsUAOtalgSExBSSwWZrBQA" +
                "AYAiGMAYAOmRkrsABRj9oYMFJEsWAaDySwnHAa4RLCwTObADAMvvBjhkExnB0w2eLcqn/x4eSwcCcAwAYeB65GwD3hKKTiMHEhsDEccHAwgVixSgTAJWQgMRewvuCW8PBwsGlwwCZ1RL8" +
                "MCgAlFB6m859wZHEwMYHiMeGw4TCwMOTWsGTQgEXmlzBwsEA4NomOBEAtp3h/vt3/sAn/v366RIEjZyeiMLBlwTCxYd3wh4A955sf5SHw2rEwIvBBcL7hP7AwcP/n80A1uQoNUcHAM5gMD" +
                "lzHQCCslfDB5XBgKbDw8HCxAbGx4XCwsDDwsEFBATTtSf6/RAAkbZLhFfE/8LBnQGOEUZbtkx4gZMBw4mGw8DEwQQAVLdN0AUAjLlQn+cAofmqxsrFxMIGxcKBxcTDwsPEAMbHgsPBw8HC" +
                "xAHGxYPDwcIDAKkCQ/leAfPLsWTDvIDE34h+fAoAxRJGLbv/+//6AwAn5ES5BxDfEEz9ODgDRirnQ3ChHsXw5/uCesCXhsJJfMU0/sKSBgDDMlbEuv/8BxDbAoz++XUPED0EQJIHwmjSww" +
                "8QQwQ6B8GWhsKTwMHDB9UEFnbCa8IEEB/sJFhBEQcrJMFpBgMURC00wQQQQ/0egEUREEAiwgXVr2Q7VAA=");
            //zkfp2.DBMerge(mDBHandle, FPBufferTest, FPBufferTest, FPBufferTest, RegTmp, ref cbRegTmp);
            if (zkfp2.DBAdd(mDBHandle, iFid, FPBufferTest) == zkfp.ZKFP_ERR_OK)
            {
                iFid += 1;
            }

            cbRegTmp = 100;

            //int fid1 = 0, score1 = 0;

            //var _res = zkfp2.DBIdentify(mDBHandle, CapTmp, ref fid1, ref score1);
        }
    }
}
