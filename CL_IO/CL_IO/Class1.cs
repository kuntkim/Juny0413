using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace CL_IO
{
    public interface IF_AdventechCtrl
    {
        // bool Init()
        // bool Run()
    }

    // -------------------------------------------- Adventech Board -----------------------------------------

    #region [ Adventech PCI Board ]
    public enum eDO_Cmd { On, Off, Toggle }

    public enum eAIO_ValueRangeType
    {
        V_OMIT = -1,
        V_Neg15To15 = 0,
        V_Neg10To10 = 1,
        V_Neg5To5 = 2,
        V_Neg2pt5To2pt5 = 3,
        V_Neg1pt25To1pt25 = 4,
        V_Neg1To1 = 5,
        V_0To15 = 6,
        V_0To10 = 7,
        V_0To5 = 8,
        V_0To2pt5 = 9,
        V_0To1pt25 = 10,
        V_0To1 = 11,
    }

    public class Adventech_CardInfo
    {
        public string _DevName;
        public int _DevNo;
        public int StartNo;
        public int EndNo;
        public eAIO_ValueRangeType AOType = eAIO_ValueRangeType.V_0To10;
        public double AIO_MaxVolt = 10.0;
        public double AIO_MaxVal = 5.0;

        public Adventech_CardInfo(string iName, int iDevNo, int iStartNo, int iEndNo)
        {
            _DevName = iName;
            _DevNo = iDevNo;
            StartNo = iStartNo;
            EndNo = iEndNo;
        }

        public Adventech_CardInfo(string iName, int iDevNo, int iStartNo, int iEndNo, double iAIO_MaxVolt, double iAIO_MaxVal, eAIO_ValueRangeType iAOType = eAIO_ValueRangeType.V_0To10)
        {
            _DevName = iName;
            _DevNo = iDevNo;
            StartNo = iStartNo;
            EndNo = iEndNo;
            AOType = iAOType;
            AIO_MaxVolt = iAIO_MaxVolt;
            AIO_MaxVal = iAIO_MaxVal;
        }
    }

    #region [DI]
    public class cAD_DI
    {
        public cAD_InstantDiCtrl[] Ctrl;
        private List<Adventech_CardInfo> CardList;

        public bool Init(List<Adventech_CardInfo> iList)
        {
            bool iResult = false;
            int CheckCount = 0;

            if (iList.Count <= 0) return iResult;
            //iList.CopyTo();
            CardList = iList.ToList();
            Ctrl = new cAD_InstantDiCtrl[CardList.Count];
            for (int i = 0; i < Ctrl.Length; i++)
            {
                Ctrl[i] = new cAD_InstantDiCtrl();
                if (Ctrl[i].init(CardList[i]._DevName, CardList[i]._DevNo))
                {
                    if (Ctrl[i].Run()) CheckCount += 1;
                }
            }

            if (CardList.Count == CheckCount) iResult = true;
            return iResult;
        }

        public bool GetVal(int iTagNo)
        {
            int _CardNo = 0;
            for (int i = 0; i < CardList.Count; i++)
            {
                if (CardList[i].StartNo <= iTagNo && CardList[i].EndNo >= iTagNo)
                {
                    _CardNo = i;
                    break;
                }
            }

            return Ctrl[_CardNo].isStatus(iTagNo - CardList[_CardNo].StartNo);
        }
    }

    public class cAD_InstantDiCtrl : IF_AdventechCtrl
    {
        private Automation.BDaq.InstantDiCtrl _ctrl;
        private int _PortCount = 0;
        private bool ST_Init = false;
        public string Dev_Name = "";

        public int DataCount = 0;
        private bool[] Data;
        private byte[] PortData;

        public bool init(string iDevName, int iDevNo)
        {
            bool iReturn = false;
            try
            {
                _ctrl = new Automation.BDaq.InstantDiCtrl();
                //_ctrl.SelectedDevice = new Automation.BDaq.DeviceInformation(_DevNo);
                _ctrl.SelectedDevice = new Automation.BDaq.DeviceInformation(iDevNo, iDevName, Automation.BDaq.AccessMode.ModeRead, 0);
                ST_Init = _ctrl.Initialized;
                if (ST_Init)
                {
                    Dev_Name = _ctrl.SelectedDevice.Description;
                    _PortCount = _ctrl.Features.PortCount;

                    PortData = new byte[_PortCount];
                    DataCount = _PortCount * 8;
                    Data = new bool[DataCount];


                    iReturn = true;
                }
            }
            catch { }

            return iReturn;
        }

        public bool Run()
        {
            bool iReturn = false;
            if (_ctrl == null) return iReturn;
            if (!ST_Init) return iReturn;
            // Thread
            ReadThreadCall();
            iReturn = true;
            return iReturn;
        }

        private void ReadThreadCall()
        {
            Thread Th_Read = new Thread(new ThreadStart(Read));
            Th_Read.IsBackground = true;
            Th_Read.Start();
        }

        private void Read()
        {
            byte _ReadData = 0;
            Automation.BDaq.ErrorCode _ErrCode;

            while (true)
            {
                for (int i = 0; i < PortData.Length; i++)
                {
                    _ErrCode = _ctrl.Read(i, out _ReadData);
                    if (_ErrCode != Automation.BDaq.ErrorCode.Success) break;

                    PortData[i] = _ReadData;
                    for (int j = 0; j < 8; j++)
                    {
                        Data[(i * 8) + j] = Convert.ToBoolean((_ReadData >> j) & 0x1);
                    }
                }

                Thread.Sleep(3);
            }
        }

        public bool isStatus(int iTag)
        {
            if (Data == null) return false;
            if (Data.Length > iTag) return Data[iTag];
            else return false;
        }
    }

    #endregion

    #region [DO]
    public class cAD_DO
    {
        public cAD_InstantDoCtrl[] Ctrl;
        private List<Adventech_CardInfo> CardList;

        public bool Init(List<Adventech_CardInfo> iList)
        {
            bool iResult = false;
            int CheckCount = 0;

            if (iList.Count <= 0) return iResult;
            //iList.CopyTo();
            CardList = iList.ToList();
            Ctrl = new cAD_InstantDoCtrl[CardList.Count];
            for (int i = 0; i < Ctrl.Length; i++)
            {
                Ctrl[i] = new cAD_InstantDoCtrl();
                if (Ctrl[i].init(CardList[i]._DevName, CardList[i]._DevNo))
                {
                    if (Ctrl[i].Run()) CheckCount += 1;
                }
            }

            if (CardList.Count == CheckCount) iResult = true;
            return iResult;
        }

        public void SetVal(int iTagNo, eDO_Cmd iCmd)
        {
            int _CardNo = 0;
            for (int i = 0; i < CardList.Count; i++)
            {
                if (CardList[i].StartNo <= iTagNo && CardList[i].EndNo >= iTagNo)
                {
                    _CardNo = i;
                    break;
                }
            }

            Ctrl[_CardNo].Write(iTagNo - CardList[_CardNo].StartNo, iCmd);
        }

        public bool GetVal(int iTagNo)
        {
            int _CardNo = 0;
            for (int i = 0; i < CardList.Count; i++)
            {
                if (CardList[i].StartNo <= iTagNo && CardList[i].EndNo >= iTagNo)
                {
                    _CardNo = i;
                    break;
                }
            }

            return Ctrl[_CardNo].isStatus(iTagNo - CardList[_CardNo].StartNo);
        }
    }

    public class cAD_InstantDoCtrl : IF_AdventechCtrl
    {
        private Automation.BDaq.InstantDoCtrl _ctrl;
        private int _PortCount = 0;
        private bool ST_Init = false;
        public string Dev_Name = "";

        public int DataCount = 0;
        private bool[] Data;
        private byte[] PortData;
        private object _Lock = new object();

        public bool init(string iDevName, int iDevNo)
        {
            bool iReturn = false;
            try
            {
                _ctrl = new Automation.BDaq.InstantDoCtrl();
                //_ctrl.SelectedDevice = new Automation.BDaq.DeviceInformation(_DevNo);
                _ctrl.SelectedDevice = new Automation.BDaq.DeviceInformation(iDevNo, iDevName, Automation.BDaq.AccessMode.ModeWrite, 0);
                ST_Init = _ctrl.Initialized;
                if (ST_Init)
                {
                    Dev_Name = _ctrl.SelectedDevice.Description;
                    _PortCount = _ctrl.Features.PortCount;

                    PortData = new byte[_PortCount];
                    DataCount = _PortCount * 8;
                    Data = new bool[DataCount];

                    iReturn = true;
                }
            }
            catch { }

            return iReturn;
        }


        public bool Run()
        {
            bool iReturn = false;
            if (_ctrl == null) return iReturn;
            if (!ST_Init) return iReturn;
            initRead();
            iReturn = true;
            return iReturn;
        }

        private void initRead()
        {
            Automation.BDaq.ErrorCode _ErrCode;
            try
            {
                byte _ReadData = 0;
                for (int i = 0; i < _PortCount; i++)
                {
                    _ErrCode = _ctrl.Read(i, out _ReadData);
                    if (_ErrCode != Automation.BDaq.ErrorCode.Success) break;

                    PortData[i] = _ReadData;
                    for (int j = 0; j < 8; j++)
                    {
                        Data[(i * 8) + j] = Convert.ToBoolean((_ReadData >> j) & 0x1);
                    }
                }
            }
            catch { }
        }

        public void Write(int iTag, eDO_Cmd Cmd)
        {
            lock (_Lock)
            {
                int _Port = 0;
                int _BitNum = 0;
                int _Data = 0;
                bool _PreData = false;
                Automation.BDaq.ErrorCode _ErrCode;

                try
                {
                    if (Data == null) return;
                    if (iTag >= Data.Length) return;

                    _Port = iTag / 8;
                    _BitNum = iTag % 8;

                    _PreData = Data[iTag];
                    switch (Cmd)
                    {
                        case eDO_Cmd.On:
                            if (Data[iTag]) return;
                            Data[iTag] = true;
                            break;

                        case eDO_Cmd.Off:
                            if (!Data[iTag]) return;
                            Data[iTag] = false;
                            break;

                        case eDO_Cmd.Toggle:
                            Data[iTag] = !Data[iTag];
                            break;
                    }

                    for (int i = 0; i < 8; i++)
                    {
                        _Data |= Convert.ToInt16(Data[_Port * 8 + i]) << i;
                    }

                    PortData[_Port] = (byte)_Data;

                    _ErrCode = _ctrl.Write(_Port, (byte)PortData[_Port]);
                    if (_ErrCode != Automation.BDaq.ErrorCode.Success) Data[iTag] = _PreData;
                }
                catch { }
            }
        }

        public bool isStatus(int iTag)
        {
            if (Data == null) return false;
            if (Data.Length > iTag) return Data[iTag];
            else return false;
        }
    }

    #endregion

    #region [AO]
    public class cAD_AO
    {
        public cAD_InstantAoCtrl[] Ctrl;
        private List<Adventech_CardInfo> CardList;

        public bool Init(List<Adventech_CardInfo> iList)
        {
            bool iResult = false;
            int CheckCount = 0;

            if (iList.Count <= 0) return iResult;
            //iList.CopyTo();
            CardList = iList.ToList();
            Ctrl = new cAD_InstantAoCtrl[CardList.Count];
            for (int i = 0; i < Ctrl.Length; i++)
            {
                Ctrl[i] = new cAD_InstantAoCtrl();
                if (Ctrl[i].init(CardList[i]._DevName, CardList[i]._DevNo, CardList[i].AOType))
                {
                    if (Ctrl[i].Run()) CheckCount += 1;
                }
            }

            if (CardList.Count == CheckCount) iResult = true;
            return iResult;
        }

        public void SetVal(int iTagNo, double iSetData)
        {
            int _CardNo = 0;
            for (int i = 0; i < CardList.Count; i++)
            {
                if (CardList[i].StartNo <= iTagNo && CardList[i].EndNo >= iTagNo)
                {
                    _CardNo = i;
                    break;
                }
            }

            Ctrl[_CardNo].Write(iTagNo - CardList[_CardNo].StartNo, Data_To_Volt(_CardNo, iSetData));
        }

        public void SetVal(int iStartTagNo, int iCount, double[] iSetData)
        {
            int _CardNo = 0;
            double[] TransSetData = new double[iSetData.Length];

            for (int i = 0; i < CardList.Count; i++)
            {
                if (CardList[i].StartNo <= iStartTagNo && CardList[i].EndNo >= iStartTagNo)
                {
                    _CardNo = i;
                    break;
                }
            }

            for(int i = 0;i < iSetData.Length; i++)
            {
                TransSetData[i] = Data_To_Volt(_CardNo, iSetData[i]);
            }
            Ctrl[_CardNo].Write(iStartTagNo - CardList[_CardNo].StartNo, iCount, TransSetData);
        }

        private double Data_To_Volt(int iCardNo, double iSetVal)
        {
            double _Value;
            if (iSetVal == 0) _Value = 0;
            else
            {
                _Value = ((iSetVal / CardList[iCardNo].AIO_MaxVal) * CardList[iCardNo].AIO_MaxVolt);
                if (_Value >= CardList[iCardNo].AIO_MaxVolt) _Value = CardList[iCardNo].AIO_MaxVolt;
            }
            return _Value;
        }

        public double GetVal(int iTagNo)
        {
            int _CardNo = 0;
            for (int i = 0; i < CardList.Count; i++)
            {
                if (CardList[i].StartNo <= iTagNo && CardList[i].EndNo >= iTagNo)
                {
                    _CardNo = i;
                    break;
                }
            }

            return Volt_To_Data(_CardNo, Ctrl[_CardNo].isStatus(iTagNo - CardList[_CardNo].StartNo));
        }

        private double Volt_To_Data(int iCardNo, double iVoltVal)
        {
            double _Value;
            if (iVoltVal == 0) _Value = 0;
            else
            {
                _Value = ((iVoltVal / CardList[iCardNo].AIO_MaxVolt) * CardList[iCardNo].AIO_MaxVal);
                if (_Value >= CardList[iCardNo].AIO_MaxVal) _Value = CardList[iCardNo].AIO_MaxVal;
            }
            return _Value;
        }
    }

    public class cAD_InstantAoCtrl : IF_AdventechCtrl
    {
        private Automation.BDaq.InstantAoCtrl _ctrl;
        private int _ChCount = 0;
        private bool ST_Init = false;
        public string Dev_Name = "";

        private double[] Data;
        private object _Lock = new object();

        public bool init(string iDevName, int iDevNo, eAIO_ValueRangeType iType)
        {
            bool iReturn = false;
            try
            {
                _ctrl = new Automation.BDaq.InstantAoCtrl();
                //_ctrl.SelectedDevice = new Automation.BDaq.DeviceInformation(_DevNo);
                _ctrl.SelectedDevice = new Automation.BDaq.DeviceInformation(iDevNo, iDevName, Automation.BDaq.AccessMode.ModeWrite, 0);
                ST_Init = _ctrl.Initialized;
                if (ST_Init)
                {
                    Dev_Name = _ctrl.SelectedDevice.Description;
                    _ChCount = _ctrl.ChannelCount;

                    Data = new double[_ChCount];

                    // Ch 별 출력 형태 설정
                    for (int i = 0; i < _ChCount; i++)
                    {
                        _ctrl.Channels[i].ValueRange = (Automation.BDaq.ValueRange)iType; //Automation.BDaq.ValueRange.V_0To10;
                    }

                    iReturn = true;
                }
            }
            catch { }

            return iReturn;
        }

        public bool Run()
        {
            bool iReturn = false;
            if (_ctrl == null) return iReturn;
            if (!ST_Init) return iReturn;
            //initRead();
            iReturn = true;
            return iReturn;
        }

        public void Write(int iCh, double iData)
        {
            Automation.BDaq.ErrorCode _ErrCode;
            try
            {
                double _PreData = 0;

                if (Data == null) return;
                if (iCh >= Data.Length) return;
                if (Data[iCh] == iData) return;
                lock(_Lock)
                {
                    _PreData = Data[iCh];
                    Data[iCh] = iData;
                    _ErrCode = _ctrl.Write(iCh, iData);
                    if (_ErrCode != Automation.BDaq.ErrorCode.Success) Data[iCh] = _PreData;
                }
            }
            catch { }
        }

        public void Write(int iChStart, int iChCount, double[] iData)
        {
            Automation.BDaq.ErrorCode _ErrCode;
            try
            {
                if (Data == null) return;
                double[] _PreData = new double[iData.Length];
                lock(_Lock)
                {
                    for (int i = 0; i < iChCount; i++)
                    {
                        if (i + iChStart >= Data.Length) return;
                        _PreData[i] = Data[i + iChStart];
                        Data[i + iChStart] = iData[i];
                    }
                    _ErrCode = _ctrl.Write(iChStart, iChCount, iData);
                    if (_ErrCode != Automation.BDaq.ErrorCode.Success)
                    {
                        for (int i = 0; i < _PreData.Length; i++)
                        {
                            Data[i + iChStart] = _PreData[i];
                        }
                    }
                }
            }
            catch { }
        }

        public double isStatus(int iTag)
        {
            if (Data == null) return 0;
            if (Data.Length > iTag) return Data[iTag];
            else return 0;
        }
    }

    #endregion

    #region [AI]
    public class cAD_AI
    {
        public cAD_InstantAiCtrl[] Ctrl;
        private List<Adventech_CardInfo> CardList;

        public bool Init(List<Adventech_CardInfo> iList)
        {
            bool iResult = false;
            int CheckCount = 0;

            if (iList.Count <= 0) return iResult;
            //iList.CopyTo();
            CardList = iList.ToList();
            Ctrl = new cAD_InstantAiCtrl[CardList.Count];
            for (int i = 0; i < Ctrl.Length; i++)
            {
                Ctrl[i] = new cAD_InstantAiCtrl();
                if (Ctrl[i].init(CardList[i]._DevName, CardList[i]._DevNo, CardList[i].AOType))
                {
                    if (Ctrl[i].Run()) CheckCount += 1;
                }
            }

            if (CardList.Count == CheckCount) iResult = true;
            return iResult;
        }

        public double GetVal(int iTagNo)
        {
            int _CardNo = 0;
            for (int i = 0; i < CardList.Count; i++)
            {
                if (CardList[i].StartNo <= iTagNo && CardList[i].EndNo >= iTagNo)
                {
                    _CardNo = i;
                    break;
                }
            }

            return Volt_To_Data(_CardNo, Ctrl[_CardNo].isStatus(iTagNo - CardList[_CardNo].StartNo));  // Volt 값을 가져온다
        }

        private double Volt_To_Data(int iCardNo, double iVoltVal)
        {
            double _Value;
            if (iVoltVal == 0) _Value = 0;
            else
            {
                _Value = ((iVoltVal / CardList[iCardNo].AIO_MaxVolt) * CardList[iCardNo].AIO_MaxVal);
                if (_Value >= CardList[iCardNo].AIO_MaxVal) _Value = CardList[iCardNo].AIO_MaxVal;
            }
            return _Value;
        }
    }

    public class cAD_InstantAiCtrl : IF_AdventechCtrl
    {
        private Automation.BDaq.InstantAiCtrl _ctrl;
        private int _ChCount = 0;
        private bool ST_Init = false;
        public string Dev_Name = "";

        private double[] ChData;

        public bool init(string iDevName, int iDevNo, eAIO_ValueRangeType iType)
        {
            bool iReturn = false;
            try
            {
                _ctrl = new Automation.BDaq.InstantAiCtrl();
                //_ctrl.SelectedDevice = new Automation.BDaq.DeviceInformation(_DevNo);
                _ctrl.SelectedDevice = new Automation.BDaq.DeviceInformation(iDevNo, iDevName, Automation.BDaq.AccessMode.ModeRead, 0);
                ST_Init = _ctrl.Initialized;
                if (ST_Init)
                {
                    Dev_Name = _ctrl.SelectedDevice.Description;
                    _ChCount = _ctrl.ChannelCount;
                    ChData = new double[_ChCount];

                    // Ch 별 출력 형태 설정
                    for (int i = 0; i < _ChCount; i++)
                    {
                        _ctrl.Channels[i].ValueRange = (Automation.BDaq.ValueRange)iType; //Automation.BDaq.ValueRange.V_0To10;
                    }

                    iReturn = true;
                }
            }
            catch { }

            return iReturn;
        }


        public bool Run()
        {
            bool iReturn = false;
            if (_ctrl == null) return iReturn;
            if (!ST_Init) return iReturn;
            // Thread
            ReadThreadCall();
            iReturn = true;
            return iReturn;
        }

        private void ReadThreadCall()
        {
            Thread Th_Read = new Thread(new ThreadStart(Read));
            Th_Read.IsBackground = true;
            Th_Read.Start();
        }

        private void Read()
        {
            Automation.BDaq.ErrorCode _ErrCode;
            while (true)
            {
                try
                {
                    _ErrCode = _ctrl.Read(0, _ChCount, ChData);
                    //if (_ErrCode != Automation.BDaq.ErrorCode.Success) break;
                }
                catch { }

                Thread.Sleep(3);
            }
        }

        public double isStatus(int iTag)
        {
            if (ChData == null) return 0;
            if (ChData.Length > iTag) return ChData[iTag];
            else return 0;
        }
    }

    #endregion


    #endregion

    // -----------------------------------------------   Wago   ---------------------------------------------

    #region [ Wago ]
    public enum eWagoReadType
    {
        IN,
        OUT_IN,
    }

    public enum eWago_CMD
    {
        INIT_READ,          // First Digital Out Read
        READ_INPUT,         // Digital Input Read
        READ_OUTPUT,        // Digital Out Read
        WRITE
    }

    public class cWago_Info : ICloneable
    {
        // Wago 설정 관련
        /// <summary>
        /// 기기 IP Address
        /// </summary>
        public string IP;
        /// <summary>
        /// DI Bit 접점 갯수
        /// </summary>
        public ushort DI_Count;
        /// <summary>
        /// DI Bit 접점 갯수
        /// </summary>
        public ushort DO_Count;
        /// <summary>
        /// AI Channel 갯수
        /// </summary>
        public ushort AI_Count;
        /// <summary>
        /// AO Channel 갯수
        /// </summary>
        public ushort AO_Count;

        public object Clone()
        {
            cWago_Info Copy = new cWago_Info();
            Copy.IP = this.IP;
            Copy.DI_Count = this.DI_Count;
            Copy.DO_Count = this.DO_Count;
            Copy.AI_Count = this.AI_Count;
            Copy.AO_Count = this.AO_Count;
            return Copy;
        }
    }

    public class cWago
    {
        private cWago_Info _Info;
        private ushort _Port = 502;     // 고정
        private Master MBmaster;
        private bool _SimulationMode;   // 시뮬레이션 모드
        public bool SimulationMode { get { return _SimulationMode; } }

        // Wago 장착 DI/DO, AI/AO 갯수
        private ushort _DI_Word_Count = 0;     // DI Word 갯수
        private ushort _DO_Word_Count = 0;     // DO Word 갯수
        private ushort _AI_Count = 0;          // AI Word 갯수
        private ushort _AO_Count = 0;          // AO Word 갯수

        // 최초 Wago 로 부터 Out Data 를 읽어온다
        private bool Flag_First = false;
        // 연결 상태 및 재연결
        public bool Connected { get { return MBmaster.connected; } }
        private int _ReConnect_Cnt = 0;

        // Wago Read 시작 주소
        // 우선순위 : In(DI,AI) -> Out(DO,AO)
        // in : Analog -> Digital
        private readonly ushort Start_Addr_Input = 0;
        private readonly ushort Start_Addr_Ouput = 0;
        private readonly ushort Start_Addr_Ouput_In = 512;

        // 배열
        private bool[] _DI;
        private bool[] _DO;
        private bool[] _DO_IN;

        private short[] _AI;
        private short[] _AO;
        private short[] _AO_IN;

        // Thread 관련
        private Thread Main_Thread;
        private bool Main_Thread_Flag = false;
        private int Main_Thread_Delay = 1;
        private int CurProcessStep = 0;

        // Event & Delegate 관련 변수
        public delegate void LogMessage(string LogTxt);
        public event LogMessage OnLogMessage;                           // Log 발생 이벤트
        public delegate void ProcessingCount(int Count);
        public event ProcessingCount OnProcessingCOunt;                 // 재연결 시도 카운터

        // I/O Module 공용 함수 호출용
        private cIO_Comn_Func Comm_Func = new cIO_Comn_Func();

        public cWago(cWago_Info info)
        {
            double quotient;    // 몫
            int remainder;   // 나머지
            _Info = info.Clone() as cWago_Info;

            // Wago 장착 Card 수량 셋팅
            // DI
            quotient = System.Math.Truncate((double)_Info.DI_Count / (double)16);
            remainder = _Info.DI_Count % 16;
            if (quotient > 0)
            {
                _DI_Word_Count = (ushort)quotient;
                if (remainder != 0) _DI_Word_Count += 1;
            }
            else
            {
                _DI_Word_Count = 1;
            }

            if (_Info.DI_Count != 0) _DI = new bool[(_DI_Word_Count * 16)];

            // DO
            quotient = System.Math.Truncate((double)_Info.DO_Count / (double)16);
            remainder = _Info.DO_Count % 16;
            if (quotient > 0)
            {
                _DO_Word_Count = (ushort)quotient;
                if (remainder != 0) _DO_Word_Count += 1;
            }
            else
            {
                _DO_Word_Count = 1;
            }

            if (_Info.DO_Count != 0)
            {
                _DO = new bool[(_DO_Word_Count * 16)];
                _DO_IN = new bool[(_DO_Word_Count * 16)];
            }

            // AI
            _AI_Count = _Info.AI_Count;
            if (_AI_Count != 0) _AI = new short[_AI_Count];

            // AO
            _AO_Count = _Info.AO_Count;
            if (_AO_Count != 0)
            {
                _AO = new short[_AO_Count];
                _AO_IN = new short[_AO_Count];
            }

            //// --- 시작 주소 Set
            //// Input
            //Start_Addr_AI = 0;
            //Start_Addr_DI = _AI_Count;
            //// Out
            //Start_Addr_AO = 0;
            //Start_Addr_DO = _AO_Count;
            //// Out-In
            //Start_Addr_AO_In = _OUT_in_Start;
            //Start_Addr_DO_In = (ushort)(_OUT_in_Start + _AO_Count);
        }

        ~cWago() { Dispose(); }

        private void Dispose()
        {
            if (MBmaster != null)
            {
                MBmaster.disconnect();
                LogWrite("Connect Stop");
            }
        }

        public void Start()
        {
            try
            {
                init();
                Run_On();
            }
            catch (Exception) { }
        }

        public void Stop()
        {
            Dispose();
        }

        private void init()
        {
            try
            {
                LogWrite("Attempting to Connect");
                MBmaster = new Master(eConnectType.Async);
                MBmaster.OnResponseData += new Master.ResponseData(Func_OnResponseData);
                MBmaster.OnException += new Master.ExceptionData(Func_OnException);
                MBmaster.connect(_Info.IP, _Port);

                // 초기 AO/DO Out-In 읽기
                Flag_First = false;
            }
            catch (Exception) { }
        }

        private void Run_On()
        {
            // Thread Start
            if (Main_Thread == null ||
               !Main_Thread.IsAlive)
            {
                Main_Thread_Flag = true;
                Main_Thread = new Thread(new ThreadStart(Wago_Thread_Func));
                Main_Thread.IsBackground = true;
                Main_Thread.Start();
            }
        }

        // Wago process ---------------------------------------
        private void Wago_Thread_Func()
        {
            while (Main_Thread_Flag)
            {
                if (!_SimulationMode)
                {
                    // 시뮬레이션 모드가 아닐때
                    try
                    {
                        if (MBmaster.connected == true)
                        {
                            if (CurProcessStep == 0)
                            {
                                // DI & AI Read
                                if (Process_In()) CurProcessStep = 1;
                            }
                            else if (CurProcessStep == 1)
                            {
                                // DO-In & AO-In Read
                                if (Flag_First)
                                {
                                    // OutPut Write
                                    if (Process_Out()) CurProcessStep = 2;
                                }
                                else
                                {
                                    // OutPut - In Read
                                    if (Process_OutIn(eWago_CMD.INIT_READ))
                                    {
                                        CurProcessStep = 2;
                                        LogWrite("First Digtal/Analog OutPut Reading -> Start");
                                    }
                                }
                            }
                            else if (CurProcessStep == 2)
                            {
                                // OutPut - In Read
                                if (Process_OutIn(eWago_CMD.READ_OUTPUT)) CurProcessStep = 0;
                            }
                        }
                        else
                        {
                            // 재연결 시도
                            CurProcessStep = 0;
                            ReConnect();
                        }
                    }
                    catch (SystemException ex)
                    {
                        LogWrite(ex.ToString().Replace(Environment.NewLine, " "));
                        //_Connected = false;
                    }
                }
                else
                {
                    _ReConnect_Cnt = 0;
                }

                Thread.Sleep(Main_Thread_Delay);
            }
        }

        private void ReConnect()
        {
            float ProgressBar;
            // 재연결 시도
            if (_ReConnect_Cnt++ >= (1000 / Main_Thread_Delay) * 20)
            {
                if (MBmaster != null)
                {
                    if (MBmaster.connect(_Info.IP, _Port))
                    {
                        LogWrite("Attempting to ReConnect");
                        _ReConnect_Cnt = 0;
                        ProcessingView(100);
                        // 초기 AO/DO Out-In 읽기
                        Flag_First = false;
                    }
                }
                else
                {
                    init();
                }
            }
            else
            {
                ProgressBar = ((float)_ReConnect_Cnt / ((1000 / (float)Main_Thread_Delay) * 20)) * 100;
                ProcessingView(Convert.ToInt16(ProgressBar));
            }
        }

        private bool Process_In()
        {
            // WAGO Modbus/tcp Input Precess  ----------------------------
            // Modbus/tcp Holding Register 사용법
            // digital input Map -- wago 매칭
            // <0 ~ 511> wago digital input card 매칭 1word -- 16bit
            // <0 ~ 511> digital output & analgo output card 매칭 1word -- 16bit
            // <512 ~  > digital output & anlalog ouput feedback input data
            // MBmaster.ReadHoldingRegister(3, 0, 1) 1-> ID 리시버 번호 두 2-> start adress 3-> 갯수

            // DI & AI Read
            if (_AI_Count + _Info.DI_Count == 0) return true;
            try
            {
                return MBmaster.ReadHoldingRegister((ushort)eWago_CMD.READ_INPUT, Start_Addr_Input, (ushort)(_AI_Count + _DI_Word_Count));
            }
            catch (Exception) { }

            return false;
        }

        // DO
        private bool Process_Out()
        {
            byte[] Total_Value = new byte[(_AO_Count + _DO_Word_Count) * 2];
            byte[] T_AO = new byte[(_AO_Count) * 2];
            byte[] T_DO = new byte[(_DO_Word_Count) * 2];

            // WAGO Modbus/tcp Output Process ------------
            // Modbus/tcp Holding Register 사용법
            // digital input Map -- wago 매칭
            // <0 ~ 511> wago digital input card 매칭 1word -- 16bit
            // <0 ~ 511> digital output & analgo output card 매칭 1word -- 16bit
            // <512 ~  > digital output & anlalog ouput feedback input data
            // MBmaster.ReadHoldingRegister(3, 0, 1) 1-> ID 리시버 번호 두 2-> start adress 3-> 갯수

            // DO-In & AO-In Read
            if (_AO_Count + _Info.DO_Count == 0) return true;
            try
            {
                // AO
                if (_AO_Count != 0) T_AO = WordToByteConvert(_AO_Count);
                // DO
                T_DO = BitToByteConvert(_DO_Word_Count);
                T_DO = Comm_Func.ByteToWordBigEnd_Convert(T_DO);  // 상하 Byte 반전

                // 통합
                if (_AO_Count != 0)
                {
                    // AO
                    for (int i = 0; i < T_AO.Length; i++) { Total_Value[i] = T_AO[i]; }
                    // DO
                    for (int i = T_AO.Length; i < Total_Value.Length; i++) { Total_Value[i] = T_DO[i - T_AO.Length]; }
                }
                else
                {
                    // DO
                    for (int i = 0; i < Total_Value.Length; i++) { Total_Value[i] = T_DO[i]; }
                }

                //
                return MBmaster.WriteMultipleRegister((ushort)eWago_CMD.WRITE, Start_Addr_Ouput, Total_Value);
            }
            catch (Exception) { }

            return false;
        }

        private bool Process_OutIn(eWago_CMD iCmd)
        {
            // WAGO Modbus/tcp Input Precess  ----------------------------
            // DO & AO Out-In Read
            if (_AO_Count + _Info.DO_Count == 0) return true;
            try
            {
                return MBmaster.ReadHoldingRegister((ushort)iCmd, Start_Addr_Ouput_In, (ushort)(_AO_Count + _DO_Word_Count));
            }
            catch (Exception) { }

            return false;
        }

        // ------------------------------------------------------------------------
        // Modbus/tcp Event for response data
        // ------------------------------------------------------------------------
        private void Func_OnResponseData(ushort ID, byte function, byte[] values)
        {
            // ------------------------------------------------------------------
            // Ignore watchdog response data
            if (ID == 0xFF) return;

            // ------------------------------------------------------------------
            // Simulation Mode Check
            if (_SimulationMode) return;

            // ------------------------------------------------------------------
            // Identify requested data
            switch (ID)
            {
                case (ushort)eWago_CMD.READ_OUTPUT:
                case (ushort)eWago_CMD.INIT_READ:
                    // Digital Out-In Read
                    Read_OutIn(values, ID);
                    break;

                case (ushort)eWago_CMD.READ_INPUT:
                    // Digistal Input Read
                    // Analog Input Read
                    Read_Input(values);

                    break;

                case (ushort)eWago_CMD.WRITE:
                    // Digital Out-In Read

                    break;
            }
        }

        // ------------------------------------------------------------------------
        // Modbus TCP slave exception
        // ------------------------------------------------------------------------
        private void Func_OnException(ushort id, byte function, byte exception, string iText)
        {
            // ------------------------------------------------------------------
            string exc = "Modbus says error: ";
            switch (exception)
            {
                case Master.excIllegalFunction: exc += "Illegal function!"; break;
                case Master.excIllegalDataAdr: exc += "Illegal data adress!"; break;
                case Master.excIllegalDataVal: exc += "Illegal data value!"; break;
                case Master.excSlaveDeviceFailure: exc += "Slave device failure!"; break;
                case Master.excAck: exc += "Acknoledge!"; break;
                case Master.excSlaveIsBusy: exc += "Slave is busy!"; break;
                case Master.excGatePathUnavailable: exc += "Gateway path unavailbale!"; break;
                case Master.excExceptionTimeout: exc += "Slave timed out!"; break;
                case Master.excExceptionConnectionLost: exc += "Connection is lost!"; break;
                case Master.excExceptionNotConnected: exc += "Not connected!"; break;
            }

            if (id == 99) exc += iText;
            LogWrite(exc);
        }

        public void CallReset() { MBmaster.ErrReset(); }

        public bool isErrStatus() { return MBmaster.isErrStatus(); }

        public bool isFirstOutInCheck() { return Flag_First; }

        // ------------------------------------------------------------    Function   -----------------------------------------------------
        //
        private void Read_OutIn(byte[] idata, ushort iID)
        {
            // 수정
            int size = idata.Length;

            if ((size % 2) != 0)
            {
                /* Error (홀수 Byte 가 들어올수 없다) */
                return;
            }
            else
            {
                int bufPos = 0;
                int Bit_Count = 16;
                int DO_Start = 0;
                short[] w_data = new short[idata.Length / 2];
                byte[] bt = new byte[2];

                // short 배열로 이동
                // Analog + Digital
                for (int i = 0; i < w_data.Length; i++)
                {
                    Array.Copy(idata, bufPos, bt, 0, 2);
                    bufPos += 2;
                    Array.Reverse(bt);
                    w_data[i] = BitConverter.ToInt16(bt, 0);
                }

                // Analog Out-In  (Analog Card 가 있다면 선두값은 Analog)
                if (_AO_Count != 0)
                {
                    for (int i = 0; i < _AO_Count; i++)
                    {
                        _AO_IN[i] = w_data[i];
                    }
                }

                // Digital Out-in (Analog Card 가 있다면 Analog 값 이후)
                if (_Info.DO_Count != 0)
                {
                    for (int i = _AO_Count; i < w_data.Length; i++)
                    {
                        for (int k = 0; k < Bit_Count; k++)
                        {
                            if ((w_data[i] & (1 << k)) == 0)
                                _DO_IN[(DO_Start * Bit_Count) + k] = false;
                            else
                                _DO_IN[(DO_Start * Bit_Count) + k] = true;
                        }

                        DO_Start += 1;
                    }
                }

                if (iID == (ushort)eWago_CMD.INIT_READ)
                {
                    First_Read_OutIn();
                }
            }
        }

        private void First_Read_OutIn()
        {
            // Out-in -> Out
            // Analog
            if (_AO_Count != 0)
            {
                lock (_AO)
                {
                    for (int i = 0; i < _AO_IN.Length; i++) { _AO[i] = _AO_IN[i]; }
                }
            }

            // Digital
            if (_Info.DO_Count != 0)
            {
                lock (_DO)
                {
                    for (int i = 0; i < _DO_IN.Length; i++) { _DO[i] = _DO_IN[i]; }
                }
            }

            //
            Flag_First = true;
            LogWrite("First Digtal/Analog OutPut Reading -> End");
        }

        // Function
        private void Read_Input(byte[] idata)
        {
            // 수정
            int size = idata.Length;

            if ((size % 2) != 0)
            {
                /* Error (홀수 Byte 가 들어올수 없다) */
                return;
            }
            else
            {
                int bufPos = 0;
                int Bit_Count = 16;
                int DI_Start = 0;
                short[] w_data = new short[idata.Length / 2];
                byte[] bt = new byte[2];

                // short 배열로 이동
                // Analog + Digital
                for (int i = 0; i < w_data.Length; i++)
                {
                    Array.Copy(idata, bufPos, bt, 0, 2);
                    bufPos += 2;
                    Array.Reverse(bt);
                    w_data[i] = BitConverter.ToInt16(bt, 0);
                }

                // 1. Analog Out-In (Analog Card 가 있다면 선두값은 Analog)
                if (_AI_Count != 0)
                {
                    for (int i = 0; i < _AI_Count; i++) { _AI[i] = w_data[i]; }
                }

                // 2. Digital Out-in (Analog Card 가 있다면 Analog 값 이후)
                if (_Info.DI_Count != 0)
                {
                    for (int i = _AI_Count; i < w_data.Length; i++)
                    {
                        for (int k = 0; k < Bit_Count; k++)
                        {
                            if ((w_data[i] & (1 << k)) == 0) _DI[(DI_Start * Bit_Count) + k] = false;
                            else _DI[(DI_Start * Bit_Count) + k] = true;
                        }
                        DI_Start += 1;
                    }
                }
            }
        }

        private byte[] BitToByteConvert(int count)
        {
            byte dat;
            byte[] data = new byte[count * 2];

            if (_DO != null)
            {
                for (int i = 0; i < count; i++)
                {
                    data[i] = 0;
                    for (int k = 0; k < 8; k++)
                    {
                        if (_DO[((i * 8) + k)] == true)
                        {
                            dat = 1;
                            data[i] = (byte)((int)data[i] | (int)(dat << k));
                        }
                        else
                        {
                            data[i] = (byte)((int)data[i] & (int)~(1 << k));
                        }
                    }
                }
            }

            return data;
        }

        private byte[] WordToByteConvert(int count)
        {
            byte[] data = new byte[count * 2];

            byte[] bt = new byte[2];
            for (int i = 0; i < count; i++)
            {
                bt = BitConverter.GetBytes(_AO[i]);
                Array.Reverse(bt);
                data[(i * 2)] = bt[0];
                data[(i * 2) + 1] = bt[1];
            }

            return data;
        }

        private void LogWrite(string Txt)
        {
            try
            {
                // Data Log
                OnLogMessage?.BeginInvoke(Txt, null, null);
            }
            catch { }
        }

        private void ProcessingView(int iCount)
        {
            try
            {
                // Data Log
                OnProcessingCOunt?.BeginInvoke(iCount, null, null);
            }
            catch { }
        }

        // ---------------------------- [Cmd 설정]
        // Digital
        public void On(int index)
        {
            if (_DO != null)
            {
                lock (_DO)
                {
                    _DO[index] = true;
                    if (_SimulationMode) _DO_IN[index] = true;
                }
            }
        }
        // Analog
        public void On(int index, short iValue)
        {
            if (_AO != null)
            {
                lock (_AO)
                {
                    _AO[index] = iValue;
                    if (_SimulationMode) _AO_IN[index] = iValue;
                }
            }
        }

        public void Off(int index)
        {
            if (_DO != null)
            {
                lock (_DO)
                {
                    _DO[index] = false;
                    if (_SimulationMode) _DO_IN[index] = false;
                }
            }
        }

        public void Toggle(int index)
        {
            if (_DO != null)
            {
                lock (_DO)
                {
                    _DO[index] = !_DO_IN[index];
                    if (_SimulationMode) _DO_IN[index] = _DO[index];
                }
            }
        }

        // ---------------------------- [Status 반환]
        // Digital
        public bool DStatus(eWagoReadType Type, int index)
        {
            bool Value = false;

            switch (Type)
            {
                case eWagoReadType.IN:
                    if (_DI != null) Value = _DI[index];
                    break;

                case eWagoReadType.OUT_IN:
                    if (_DO_IN != null) Value = _DO_IN[index];
                    break;
            }

            return Value;
        }
        // Analog
        public short AStatus(eWagoReadType Type, int index)
        {
            short Value = 0;

            switch (Type)
            {
                case eWagoReadType.IN:
                    if (_AI != null) Value = _AI[index];
                    break;

                case eWagoReadType.OUT_IN:
                    if (_AO_IN != null) Value = _AO_IN[index];
                    break;
            }

            return Value;
        }

        // -----------------------------------------------------    Simulation
        public void Set_SimulationMode(bool iFlag)
        {
            _SimulationMode = iFlag;
            if (_SimulationMode)
            {
                // Simulation Mode 를 하게 되면 Wago 연결을 끊는다
                LogWrite("Simulation Mode -> On");
                Stop();
            }
            else
            {
                LogWrite("Simulation Mode -> Off");
            }
        }

        // Digital
        public void Simul_On(int index)
        {
            if (_DI != null)
            {
                if (_SimulationMode)
                {
                    lock (_DI) { _DI[index] = true; }
                }
            }
        }

        // Analog
        public void Simul_On(int index, short iValue)
        {
            if (_AI != null)
            {
                if (_SimulationMode)
                {
                    lock (_AI) { _AI[index] = iValue; }
                }
            }
        }

        public void Simul_Off(int index)
        {
            if (_DI != null)
            {
                if (_SimulationMode)
                {
                    lock (_DI) { _DI[index] = false; }
                }
            }
        }
    }
    #endregion

    #region [ 공용 함수 ]
    public class cIO_Comn_Func
    {
        public byte[] ByteToWordBigEnd_Convert(byte[] data)
        {
            byte temp;

            for (int i = 0; i < data.Length; i += 2)
            {
                temp = data[i];
                data[i] = data[i + 1];
                data[i + 1] = temp;
            }

            return data;
        }
    }

    #endregion

}
