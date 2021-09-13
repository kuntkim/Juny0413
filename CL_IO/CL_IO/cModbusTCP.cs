using System;
using System.Collections.Generic;
using System.Collections;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace CL_IO
{
    /// <summary>
    /// Modbus Tcp Connect Type Set
    /// Async
    /// Sync
    /// </summary>
    public enum eConnectType : uint
    {
        /// <summary> Async Connect </summary>
        Async = 0x00,
        /// <summary> Sync Connect </summary>
        Sync = 0x01
    }

    public enum eWagoCmdMode
    {
        NonResponse,    // 명령에 대한 응답이 필요 없음
        Response,       // 명령에 대한 응답이 필요
    }

    public class cWagoCmdMsg
    {
        public ushort ID;                                       // 명령어 코드
        public byte[] CmdData;                                  // 명령어
        public eWagoCmdMode Mode = eWagoCmdMode.NonResponse;    // NonResponse or Response 구분자

        public void SetData(cWagoCmdMsg iSetData)
        {
            ID = iSetData.ID;
            CmdData = iSetData.CmdData;
            Mode = iSetData.Mode;
        }
    }

    internal class cMsgManager
    {
        private Master _Parent;
        //
        private int _ResponseTimeOut = 50;      // 응답에 대한 타임아웃 (ms)
        private int _ResponseErrRetryCount = 5; // 응답에 대한 Retry카운트
        private int _MaxBuffCount = 10;

        // Write 명령 처리 (Buffer 에 담아서 순차처리)
        private List<cWagoCmdMsg> CmdBuffer = new List<cWagoCmdMsg>();

        // Write 에 대한 응답
        private DateTime WriteTime = default(DateTime);
        private bool WaitingResponse = false;
        // Err 처리 (Err 발생시 Reset 전에는 Write 명령이 실행되지 않음)
        private int _ResponseErrCurCount = 0;
        public int ResponseErrCurCount { get { return _ResponseErrCurCount; } }
        private bool _ResponseErrStatus;
        public bool ResponseErrStatus { get { return _ResponseErrStatus; } }

        // Cmd Thread
        private Thread Thread_MsgWrite;
        private bool Thread_MsgWrite_Flag = false;

        private object obLock = new object();

        public cMsgManager(Master iParent, int iTimeOutms, int iErrRetryCount, int iMaxBufferCount)
        {
            _Parent = iParent;
            //
            _ResponseTimeOut = iTimeOutms;
            _ResponseErrRetryCount = iErrRetryCount;
            _MaxBuffCount = iMaxBufferCount;

            _ResponseErrStatus = false;
            _ResponseErrCurCount = 0;
            WaitingResponse = false;
        }

        ~cMsgManager() { Disponse(); }
        public void Disponse()
        {
            Thread_MsgWrite_Flag = false;
            Thread_MsgWrite.Join();
            CmdBuffer.Clear();
            _ResponseErrStatus = false;
            ResponseOK();
        }

        public void Run()
        {
            if (Thread_MsgWrite == null ||
                Thread_MsgWrite.IsAlive == false)
            {
                // Command Buffer 를 하나씩 빼서 실행하는 Thread 
                Thread_MsgWrite_Flag = true;
                Thread_MsgWrite = new Thread(new ThreadStart(FuncMsgWrite));
                Thread_MsgWrite.IsBackground = true;
                Thread_MsgWrite.Start();
            }
        }

        public bool Insert(cWagoCmdMsg iData, bool iForce)
        {
            if (_ResponseErrStatus) return false;
            //if (String.IsNullOrEmpty(iData.CmdData)) return false;
            if (!Thread_MsgWrite_Flag) return false;

            try
            {
                // Command 버퍼 삽입
                if (iForce)
                {
                    // 버퍼 갯수 상관없이 삽입
                    lock (obLock) CmdBuffer.Add(iData);
                }
                else
                {
                    // 버퍼 갯수가 20개 이하 일 경우에 삽입 (상시리딩명령)
                    if (WaitingResponse) return false;
                    if (CmdBuffer.Count < _MaxBuffCount)
                    {
                        lock (obLock) CmdBuffer.Add(iData);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                string _Err = ex.ToString();
                return false;
            }
        }

        private void FuncMsgWrite()
        {
            cWagoCmdMsg _iData;
            TimeSpan WCompareTime;

            while (Thread_MsgWrite_Flag)
            {
                lock (obLock)
                {
                    if (!_ResponseErrStatus)
                    {
                        // 1. 응답 대기가 아닐때
                        if (!WaitingResponse)
                        {
                            // Command 버퍼에 데이터가 있을때
                            if (CmdBuffer.Count >= 1)
                            {
                                _iData = CmdBuffer[0];
                                CmdBuffer.RemoveAt(0);

                                if (_iData.Mode == eWagoCmdMode.NonResponse) WaitingResponseSet(false);  // Command 후 완료
                                else WaitingResponseSet(true);   // Command 에 대한 응답대기

                                // 전송
                                _Parent.SocketWriteData(_iData);
                            }
                        }
                        else
                        {
                            // 2. 응답 대기 일때
                            WCompareTime = DateTime.Now - WriteTime;
                            if (WCompareTime.TotalMilliseconds >= _ResponseTimeOut)
                            {
                                // 응답에 대한 처리 -> NG
                                ResponseErrSwitch(true);
                                WaitingResponseSet(false);
                                // Init
                                //_Parent.BufferClear();
                            }
                        }
                    }
                }

                Thread.Sleep(1);
            }
        }

        public void ResponseOK()
        {
            lock (obLock)
            {
                ResponseErrSwitch(false);
                WaitingResponseSet(false);
            }
        }

        private void WaitingResponseSet(bool iFlag)
        {
            WaitingResponse = iFlag;
            WriteTime = iFlag ? DateTime.Now : default(DateTime);
        }

        private void ResponseErrSwitch(bool iFlag)
        {
            StringBuilder ErrTxt = new StringBuilder();
            if (iFlag)
            {
                if (!_ResponseErrStatus)
                {
                    if (_ResponseErrCurCount >= _ResponseErrRetryCount)
                    {
                        // Error 처리
                        _ResponseErrStatus = true;
                        _ResponseErrCurCount = 0;
                    }
                    else
                    {
                        _ResponseErrCurCount += 1;
                    }

                    // Err Message 전달
                    ErrTxt.Append(" ErrCount : [");
                    ErrTxt.Append(_ResponseErrCurCount.ToString());
                    ErrTxt.Append("] ");
                    ErrTxt.Append("Time : [");
                    ErrTxt.Append(DateTime.Now.ToString("HH:mm:ss.fffff"));
                    ErrTxt.Append("] ");

                    _Parent.CallException(99, 99, 99, ErrTxt.ToString());
                }
            }
            else
            {
                //_ResponseErrStatus = false;  // 한번 ResponseErr 상태로 확인이 되면 Reset 전에는 Err 상태를 유지
                _ResponseErrCurCount = 0;
            }
        }

        public void ErrReset()
        {
            // Err Reset
            if (_ResponseErrStatus)
            {
                CmdBuffer.Clear();
                _ResponseErrCurCount = 0;
                _ResponseErrStatus = false;
            }
        }
    }

    /// <summary>
    /// Modbus TCP common driver class. This class implements a modbus TCP master driver.
    /// It supports the following commands:
    /// 
    /// Read coils
    /// Read discrete inputs
    /// Write single coil
    /// Write multiple cooils
    /// Read holding register
    /// Read input register
    /// Write single register
    /// Write multiple register
    /// 
    /// All commands can be sent in synchronous or asynchronous mode. If a value is accessed
    /// in synchronous mode the program will stop and wait for slave to response. If the 
    /// slave didn't answer within a specified time a timeout exception is called.
    /// The class uses multi threading for both synchronous and asynchronous access. For
    /// the communication two lines are created. This is necessary because the synchronous
    /// thread has to wait for a previous command to finish.
    /// 
    /// </summary>
    /// 
    public class Master
    {
        private eConnectType _ConnectType = eConnectType.Async;
        // ------------------------------------------------------------------------
        // Constants for access
        private const byte fctReadCoil = 0x01;                      // Reading of several single input bits ,                               R: Process image
        private const byte fctReadDiscreteInputs = 0x02;            // Reading of several input bits ,                                      R: Process image
        private const byte fctReadHoldingRegister = 0x03;           // Reading of several input registers (Word) ,                          R: Process image , internal variables
        private const byte fctReadInputRegister = 0x04;             // Reading of several input registers (Word) ,                          R: Process image , internal variables
        private const byte fctWriteSingleCoil = 0x05;               // Writing of an individual output bit ,                                W: Process image
        private const byte fctWriteSingleRegister = 0x06;           // Writing of an individual output register (Word) ,                    W: Process image , internal variables   
        private const byte fctGetCommEventCounters = 0x0b;          // Communication event counter ,                                        R: None
        private const byte fctWriteMultipleCoils = 0x0f;            // Writing of several output bits ,                                     W: Process image
        private const byte fctWriteMultipleRegister = 0x10;         // Writing of several output registers (Word) ,                         W: Process image , internal variables
        private const byte fctMaskWriteRegister = 0x16;             // Writing of several bits of an individual output register by mask ,   W: Process image
        private const byte fctReadWriteMultipleRegister = 0x17;     // Reading and writing of several output registers ,                    R/W: Process image

        /// <summary>Constant for exception illegal function.</summary>
        public const byte excIllegalFunction = 1;
        /// <summary>Constant for exception illegal data address.</summary>
        public const byte excIllegalDataAdr = 2;
        /// <summary>Constant for exception illegal data value.</summary>
        public const byte excIllegalDataVal = 3;
        /// <summary>Constant for exception slave device failure.</summary>
        public const byte excSlaveDeviceFailure = 4;
        /// <summary>Constant for exception acknowledge.</summary>
        public const byte excAck = 5;
        /// <summary>Constant for exception slave is busy/booting up.</summary>
        public const byte excSlaveIsBusy = 6;
        /// <summary>Constant for exception gate path unavailable.</summary>
        public const byte excGatePathUnavailable = 10;
        /// <summary>Constant for exception not connected.</summary>
        public const byte excExceptionNotConnected = 253;
        /// <summary>Constant for exception connection lost.</summary>
        public const byte excExceptionConnectionLost = 254;
        /// <summary>Constant for exception response timeout.</summary>
        public const byte excExceptionTimeout = 255;
        /// <summary>Constant for exception wrong offset.</summary>
        private const byte excExceptionOffset = 128;
        /// <summary>Constant for exception send failt.</summary>
        private const byte excSendFailt = 100;

        // ------------------------------------------------------------------------
        // Private declarations
        private static ushort _timeout = 500;
        private static ushort _refresh = 10;
        private static bool _connected = false;

        private Socket tcpAsyCl;
        private byte[] tcpAsyClBuffer = new byte[2048];

        private Socket tcpSynCl;
        private byte[] tcpSynClBuffer = new byte[2048];

        // ------------------------------------------------------------------------
        // Write Manager
        private cMsgManager _Manager;
        private bool ConnectingFlag = false;

        // ------------------------------------------------------------------------
        /// <summary>Response data event. This event is called when new data arrives</summary>
        public delegate void ResponseData(ushort id, byte function, byte[] data);
        /// <summary>Response data event. This event is called when new data arrives</summary>
        public event ResponseData OnResponseData;
        /// <summary>Exception data event. This event is called when the data is incorrect</summary>
        public delegate void ExceptionData(ushort id, byte function, byte exception, string Txt);
        /// <summary>Exception data event. This event is called when the data is incorrect</summary>
        public event ExceptionData OnException;

        // ------------------------------------------------------------------------
        /// <summary>Response timeout. If the slave didn't answers within in this time an exception is called.</summary>
        /// <value>The default value is 500ms.</value>
        public ushort timeout
        {
            get { return _timeout; }
            set { _timeout = value; }
        }

        // ------------------------------------------------------------------------
        /// <summary>Refresh timer for slave answer. The class is polling for answer every X ms.</summary>
        /// <value>The default value is 10ms.</value>
        public ushort refresh
        {
            get { return _refresh; }
            set { _refresh = value; }
        }

        // ------------------------------------------------------------------------
        /// <summary>Shows if a connection is active.</summary>
        public bool connected { get { return _connected; } }

        // ------------------------------------------------------------------------
        /// <summary>Create master instance with parameters.</summary>
        /// <param name="ip">IP adress of modbus slave.</param>
        /// <param name="port">Port number of modbus slave. Usually port 502 is used.</param>
        /// <param name="_Type">Modbus Connect Type Setting</param>
        public Master(eConnectType _Type = eConnectType.Async)
        {
            _ConnectType = _Type;
            _Manager = new cMsgManager(this, 50, 5, 10);
        }

        // ------------------------------------------------------------------------
        /// <summary>Start connection to slave.</summary>
        /// <param name="ip">IP adress of modbus slave.</param>
        /// <param name="port">Port number of modbus slave. Usually port 502 is used.</param>
        public bool connect(string ip, ushort port)
        {

            try
            {
                IPAddress _ip;
                if (IPAddress.TryParse(ip, out _ip) == false)
                {
                    IPHostEntry hst = Dns.GetHostEntry(ip);
                    ip = hst.AddressList[0].ToString();
                }

                if (ConnectingFlag) return false;
                switch (_ConnectType)
                {
                    case eConnectType.Async:
                        // ----------------------------------------------------------------
                        // Connect asynchronous client
                        tcpAsyCl = new Socket(IPAddress.Parse(ip).AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                        tcpAsyCl.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendTimeout, _timeout);
                        tcpAsyCl.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, _timeout);
                        tcpAsyCl.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.NoDelay, 1);
                        IAsyncResult _ar = tcpAsyCl.BeginConnect(new IPEndPoint(IPAddress.Parse(ip), port), new AsyncCallback(ConnectAsync), tcpAsyCl);
                        //tcpAsyCl.Connect(new IPEndPoint(IPAddress.Parse(ip), port));
                        //tcpAsyCl.BeginReceive(tcpAsyClBuffer, 0, tcpAsyClBuffer.Length, SocketFlags.None, new AsyncCallback(OnReceive), tcpAsyCl);

                        //if (_ar.AsyncWaitHandle.WaitOne(5000, false)) _Manager.Run();
                        //else                                            Dispose();
                        break;

                    case eConnectType.Sync:
                        // ----------------------------------------------------------------
                        // Connect synchronous client
                        tcpSynCl = new Socket(IPAddress.Parse(ip).AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                        tcpSynCl.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendTimeout, _timeout);
                        tcpSynCl.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, _timeout);
                        tcpSynCl.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.NoDelay, 1);
                        tcpSynCl.Connect(new IPEndPoint(IPAddress.Parse(ip), port));
                        break;
                }

                _Manager.Run();
                ConnectingFlag = true;
            }
            catch (Exception)
            {
                _connected = false;
            }

            return true;
        }

        private void ConnectAsync(IAsyncResult result)
        {
            Socket _Sock = (Socket)result.AsyncState;
            try
            {
                _Sock.EndConnect(result);
                // ----------------------------------------------------------------
                // Connect asynchronous client
                tcpAsyCl.BeginReceive(tcpAsyClBuffer, 0, tcpAsyClBuffer.Length, SocketFlags.None, new AsyncCallback(OnReceive), tcpAsyCl);
                _connected = true;
            }
            catch (Exception)
            {
                _connected = false;
                Dispose();
            }

            ConnectingFlag = false;
        }

        // ------------------------------------------------------------------------
        /// <summary>Stop connection to slave.</summary>
        public void disconnect()
        {
            Dispose();
        }

        // ------------------------------------------------------------------------
        /// <summary>Destroy master instance.</summary>
        ~Master()
        {
            Dispose();
        }

        // ------------------------------------------------------------------------
        /// <summary>Destroy master instance</summary>
        private void Dispose()
        {
            // Connect asynchronous client
            if (_Manager != null) _Manager.Disponse();
            if (tcpAsyCl != null)
            {
                if (tcpAsyCl.Connected)
                {
                    try { tcpAsyCl.Shutdown(SocketShutdown.Both); }
                    catch { }
                    tcpAsyCl.Close();
                }
                tcpAsyCl = null;
            }

            // Connect synchronous client
            if (tcpSynCl != null)
            {
                if (tcpSynCl.Connected)
                {
                    try { tcpSynCl.Shutdown(SocketShutdown.Both); }
                    catch { }
                    tcpSynCl.Close();
                }
                tcpSynCl = null;
            }

            _connected = false;
        }

        public int isErrCount() { return _Manager.ResponseErrCurCount; }

        public void ErrReset() { _Manager.ErrReset(); }

        public bool isErrStatus() { return _Manager.ResponseErrStatus; }

        internal void CallException(ushort id, byte function, byte exception, string iText = "")
        {
            if (exception == excExceptionConnectionLost)
            {
                //tcpSynCl = null;
                //tcpAsyCl = null;
                Dispose();
            }

            //
            OnException?.BeginInvoke(id, function, exception, iText, null, null);
        }

        // ------------------------------------------------------------------------
        /// <summary>Read coils from slave asynchronous. The result is given in the response function.</summary>
        /// <param name="id">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
        /// <param name="startAddress">Address from where the data read begins.</param>
        /// <param name="numInputs">Length of data.</param>
        public bool ReadCoils(ushort id, ushort startAddress, ushort numInputs)
        {
            cWagoCmdMsg _Msg = new cWagoCmdMsg();
            _Msg.CmdData = CreateReadHeader(id, startAddress, numInputs, fctReadCoil);
            _Msg.ID = id;
            _Msg.Mode = eWagoCmdMode.Response;

            return WriteAsyncData(_Msg);
        }

        // ------------------------------------------------------------------------
        /// <summary>Read coils from slave synchronous.</summary>
        /// <param name="id">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
        /// <param name="startAddress">Address from where the data read begins.</param>
        /// <param name="numInputs">Length of data.</param>
        /// <param name="values">Contains the result of function.</param>
        public void ReadCoils(ushort id, ushort startAddress, ushort numInputs, ref byte[] values)
        {
            values = WriteSyncData(CreateReadHeader(id, startAddress, numInputs, fctReadCoil), id);
        }

        // ------------------------------------------------------------------------
        /// <summary>Read discrete inputs from slave asynchronous. The result is given in the response function.</summary>
        /// <param name="id">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
        /// <param name="startAddress">Address from where the data read begins.</param>
        /// <param name="numInputs">Length of data.</param>
        public bool ReadDiscreteInputs(ushort id, ushort startAddress, ushort numInputs)
        {
            cWagoCmdMsg _Msg = new cWagoCmdMsg();
            _Msg.CmdData = CreateReadHeader(id, startAddress, numInputs, fctReadDiscreteInputs);
            _Msg.ID = id;
            _Msg.Mode = eWagoCmdMode.Response;

            return WriteAsyncData(_Msg);
        }

        // ------------------------------------------------------------------------
        /// <summary>Read discrete inputs from slave synchronous.</summary>
        /// <param name="id">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
        /// <param name="startAddress">Address from where the data read begins.</param>
        /// <param name="numInputs">Length of data.</param>
        /// <param name="values">Contains the result of function.</param>
        public void ReadDiscreteInputs(ushort id, ushort startAddress, ushort numInputs, ref byte[] values)
        {
            values = WriteSyncData(CreateReadHeader(id, startAddress, numInputs, fctReadDiscreteInputs), id);
        }

        // ------------------------------------------------------------------------
        /// <summary>Read holding registers from slave asynchronous. The result is given in the response function.</summary>
        /// <param name="id">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
        /// <param name="startAddress">Address from where the data read begins.</param>
        /// <param name="numInputs">Length of data.</param>
        public bool ReadHoldingRegister(ushort id, ushort startAddress, ushort numInputs)
        {
            cWagoCmdMsg _Msg = new cWagoCmdMsg();
            _Msg.CmdData = CreateReadHeader(id, startAddress, numInputs, fctReadHoldingRegister);
            _Msg.ID = id;
            _Msg.Mode = eWagoCmdMode.Response;

            return WriteAsyncData(_Msg);
        }

        // ------------------------------------------------------------------------
        /// <summary>Read holding registers from slave synchronous.</summary>
        /// <param name="id">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
        /// <param name="startAddress">Address from where the data read begins.</param>
        /// <param name="numInputs">Length of data.</param>
        /// <param name="values">Contains the result of function.</param>
        public void ReadHoldingRegister(ushort id, ushort startAddress, ushort numInputs, ref byte[] values)
        {
            values = WriteSyncData(CreateReadHeader(id, startAddress, numInputs, fctReadHoldingRegister), id);
        }

        // ------------------------------------------------------------------------
        /// <summary>Read input registers from slave asynchronous. The result is given in the response function.</summary>
        /// <param name="id">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
        /// <param name="startAddress">Address from where the data read begins.</param>
        /// <param name="numInputs">Length of data.</param>
        public bool ReadInputRegister(ushort id, ushort startAddress, ushort numInputs)
        {
            cWagoCmdMsg _Msg = new cWagoCmdMsg();
            _Msg.CmdData = CreateReadHeader(id, startAddress, numInputs, fctReadInputRegister);
            _Msg.ID = id;
            _Msg.Mode = eWagoCmdMode.Response;

            return WriteAsyncData(_Msg);
        }

        // ------------------------------------------------------------------------
        /// <summary>Read input registers from slave synchronous.</summary>
        /// <param name="id">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
        /// <param name="startAddress">Address from where the data read begins.</param>
        /// <param name="numInputs">Length of data.</param>
        /// <param name="values">Contains the result of function.</param>
        public void ReadInputRegister(ushort id, ushort startAddress, ushort numInputs, ref byte[] values)
        {
            values = WriteSyncData(CreateReadHeader(id, startAddress, numInputs, fctReadInputRegister), id);
        }

        // ------------------------------------------------------------------------
        /// <summary>Write single coil in slave asynchronous. The result is given in the response function.</summary>
        /// <param name="id">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
        /// <param name="startAddress">Address from where the data read begins.</param>
        /// <param name="OnOff">Specifys if the coil should be switched on or off.</param>
        public bool WriteSingleCoils(ushort id, ushort startAddress, bool OnOff)
        {
            cWagoCmdMsg _Msg = new cWagoCmdMsg();

            _Msg.CmdData = CreateWriteHeader(id, startAddress, 1, 1, fctWriteSingleCoil);
            if (OnOff == true) _Msg.CmdData[10] = 255;
            else _Msg.CmdData[10] = 0;
            _Msg.ID = id;
            _Msg.Mode = eWagoCmdMode.Response;

            return WriteAsyncData(_Msg);
        }

        // ------------------------------------------------------------------------
        /// <summary>Write single coil in slave synchronous.</summary>
        /// <param name="id">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
        /// <param name="startAddress">Address from where the data read begins.</param>
        /// <param name="OnOff">Specifys if the coil should be switched on or off.</param>
        /// <param name="result">Contains the result of the synchronous write.</param>
        public void WriteSingleCoils(ushort id, ushort startAddress, bool OnOff, ref byte[] result)
        {
            byte[] data;
            data = CreateWriteHeader(id, startAddress, 1, 1, fctWriteSingleCoil);
            if (OnOff == true) data[10] = 255;
            else data[10] = 0;
            result = WriteSyncData(data, id);
        }

        // ------------------------------------------------------------------------
        /// <summary>Write multiple coils in slave asynchronous. The result is given in the response function.</summary>
        /// <param name="id">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
        /// <param name="startAddress">Address from where the data read begins.</param>
        /// <param name="numBits">Specifys number of bits.</param>
        /// <param name="values">Contains the bit information in byte format.</param>
        public bool WriteMultipleCoils(ushort id, ushort startAddress, ushort numBits, byte[] values)
        {
            cWagoCmdMsg _Msg = new cWagoCmdMsg();
            byte numBytes = Convert.ToByte(values.Length);

            _Msg.CmdData = CreateWriteHeader(id, startAddress, numBits, (byte)(numBytes + 2), fctWriteMultipleCoils);
            // Byte 13 -> Data Byte1
            // Byte 14 -> Data Byte2
            Array.Copy(values, 0, _Msg.CmdData, 13, numBytes);
            _Msg.ID = id;
            _Msg.Mode = eWagoCmdMode.Response;

            return WriteAsyncData(_Msg);
        }

        // ------------------------------------------------------------------------
        /// <summary>Write multiple coils in slave synchronous.</summary>
        /// <param name="id">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
        /// <param name="startAddress">Address from where the data read begins.</param>
        /// <param name="numBits">Specifys number of bits.</param>
        /// <param name="values">Contains the bit information in byte format.</param>
        /// <param name="result">Contains the result of the synchronous write.</param>
        public void WriteMultipleCoils(ushort id, ushort startAddress, ushort numBits, byte[] values, ref byte[] result)
        {
            byte numBytes = Convert.ToByte(values.Length);
            byte[] data;
            data = CreateWriteHeader(id, startAddress, numBits, (byte)(numBytes + 2), fctWriteMultipleCoils);
            // Byte 13 -> Data Byte1
            // Byte 14 -> Data Byte2
            Array.Copy(values, 0, data, 13, numBytes);
            result = WriteSyncData(data, id);
        }

        // ------------------------------------------------------------------------
        /// <summary>Write single register in slave asynchronous. The result is given in the response function.</summary>
        /// <param name="id">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
        /// <param name="startAddress">Address to where the data is written.</param>
        /// <param name="values">Contains the register information.</param>
        public bool WriteSingleRegister(ushort id, ushort startAddress, byte[] values)
        {
            cWagoCmdMsg _Msg = new cWagoCmdMsg();

            _Msg.CmdData = CreateWriteHeader(id, startAddress, 1, 1, fctWriteSingleRegister);
            _Msg.CmdData[10] = values[0];
            _Msg.CmdData[11] = values[1];
            _Msg.ID = id;
            _Msg.Mode = eWagoCmdMode.Response;

            return WriteAsyncData(_Msg);
        }

        // ------------------------------------------------------------------------
        /// <summary>Write single register in slave synchronous.</summary>
        /// <param name="id">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
        /// <param name="startAddress">Address to where the data is written.</param>
        /// <param name="values">Contains the register information.</param>
        /// <param name="result">Contains the result of the synchronous write.</param>
        public void WriteSingleRegister(ushort id, ushort startAddress, byte[] values, ref byte[] result)
        {
            byte[] data;
            data = CreateWriteHeader(id, startAddress, 1, 1, fctWriteSingleRegister);
            data[10] = values[0];
            data[11] = values[1];
            result = WriteSyncData(data, id);
        }

        // ------------------------------------------------------------------------
        /// <summary>Write multiple registers in slave asynchronous. The result is given in the response function.</summary>
        /// <param name="id">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
        /// <param name="startAddress">Address to where the data is written.</param>
        /// <param name="values">Contains the register information.</param>
        public bool WriteMultipleRegister(ushort id, ushort startAddress, byte[] values)
        {
            cWagoCmdMsg _Msg = new cWagoCmdMsg();
            ushort numBytes = Convert.ToUInt16(values.Length);
            if (numBytes % 2 > 0) numBytes++;

            _Msg.CmdData = CreateWriteHeader(id, startAddress, Convert.ToUInt16(numBytes / 2), Convert.ToUInt16(numBytes + 2), fctWriteMultipleRegister);
            // Byte 13 -> Data Byte1
            // Byte 14 -> Data Byte2
            Array.Copy(values, 0, _Msg.CmdData, 13, values.Length);
            _Msg.ID = id;
            _Msg.Mode = eWagoCmdMode.Response;

            return WriteAsyncData(_Msg);
        }

        // ------------------------------------------------------------------------
        /// <summary>Write multiple registers in slave synchronous.</summary>
        /// <param name="id">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
        /// <param name="startAddress">Address to where the data is written.</param>
        /// <param name="values">Contains the register information.</param>
        /// <param name="result">Contains the result of the synchronous write.</param>
        public void WriteMultipleRegister(ushort id, ushort startAddress, byte[] values, ref byte[] result)
        {
            ushort numBytes = Convert.ToUInt16(values.Length);
            if (numBytes % 2 > 0) numBytes++;
            byte[] data;

            data = CreateWriteHeader(id, startAddress, Convert.ToUInt16(numBytes / 2), Convert.ToUInt16(numBytes + 2), fctWriteMultipleRegister);
            // Byte 13 -> Data Byte1
            // Byte 14 -> Data Byte2
            Array.Copy(values, 0, data, 13, values.Length);
            result = WriteSyncData(data, id);
        }

        // ------------------------------------------------------------------------
        /// <summary>Read/Write multiple registers in slave asynchronous. The result is given in the response function.</summary>
        /// <param name="id">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
        /// <param name="startReadAddress">Address from where the data read begins.</param>
        /// <param name="numInputs">Length of data.</param>
        /// <param name="startWriteAddress">Address to where the data is written.</param>
        /// <param name="values">Contains the register information.</param>
        public bool ReadWriteMultipleRegister(ushort id, ushort startReadAddress, ushort numInputs, ushort startWriteAddress, byte[] values)
        {
            cWagoCmdMsg _Msg = new cWagoCmdMsg();
            ushort numBytes = Convert.ToUInt16(values.Length);
            if (numBytes % 2 > 0) numBytes++;

            _Msg.CmdData = CreateReadWriteHeader(id, startReadAddress, numInputs, startWriteAddress, Convert.ToUInt16(numBytes / 2));
            // Byte 17...(B+16) -> Register values (B = Byte count)
            Array.Copy(values, 0, _Msg.CmdData, 17, values.Length);
            _Msg.ID = id;
            _Msg.Mode = eWagoCmdMode.Response;

            return WriteAsyncData(_Msg);
        }

        // ------------------------------------------------------------------------
        /// <summary>Read/Write multiple registers in slave synchronous. The result is given in the response function.</summary>
        /// <param name="id">Unique id that marks the transaction. In asynchonous mode this id is given to the callback function.</param>
        /// <param name="startReadAddress">Address from where the data read begins.</param>
        /// <param name="numInputs">Length of data.</param>
        /// <param name="startWriteAddress">Address to where the data is written.</param>
        /// <param name="values">Contains the register information.</param>
        /// <param name="result">Contains the result of the synchronous command.</param>
        public void ReadWriteMultipleRegister(ushort id, ushort startReadAddress, ushort numInputs, ushort startWriteAddress, byte[] values, ref byte[] result)
        {
            ushort numBytes = Convert.ToUInt16(values.Length);
            if (numBytes % 2 > 0) numBytes++;
            byte[] data;

            data = CreateReadWriteHeader(id, startReadAddress, numInputs, startWriteAddress, Convert.ToUInt16(numBytes / 2));
            // Byte 17...(B+16) -> Register values (B = Byte count)
            Array.Copy(values, 0, data, 17, values.Length);
            result = WriteSyncData(data, id);
        }

        // ------------------------------------------------------------------------
        // Create modbus header for read action
        private byte[] CreateReadHeader(ushort id, ushort startAddress, ushort length, byte function)
        {
            byte[] data = new byte[12];

            byte[] _id = BitConverter.GetBytes((short)id);
            data[0] = _id[0];				// Slave id high byte
            data[1] = _id[1];				// Slave id low byte
            data[5] = 6;					// Message size
            data[6] = 1;					// Slave address
            data[7] = function;				// Function code
            byte[] _adr = BitConverter.GetBytes((short)IPAddress.HostToNetworkOrder((short)startAddress));
            data[8] = _adr[0];				// Start address
            data[9] = _adr[1];				// Start address
            byte[] _length = BitConverter.GetBytes((short)IPAddress.HostToNetworkOrder((short)length));
            data[10] = _length[0];			// Number of data to read
            data[11] = _length[1];			// Number of data to read
            return data;
        }

        // ------------------------------------------------------------------------
        // Create modbus header for write action
        private byte[] CreateWriteHeader(ushort id, ushort startAddress, ushort numData, ushort numBytes, byte function)
        {
            byte[] data = new byte[numBytes + 11];

            byte[] _id = BitConverter.GetBytes((short)id);
            data[0] = _id[0];				// Slave id high byte
            data[1] = _id[1];				// Slave id low byte+
            byte[] _size = BitConverter.GetBytes((short)IPAddress.HostToNetworkOrder((short)(5 + numBytes)));
            data[4] = _size[0];				// Complete message size in bytes
            data[5] = _size[1];				// Complete message size in bytes
            data[6] = 1;					// Slave address  (0x01 not Used)
            data[7] = function;				// Function code
            byte[] _adr = BitConverter.GetBytes((short)IPAddress.HostToNetworkOrder((short)startAddress));
            data[8] = _adr[0];				// Start address
            data[9] = _adr[1];				// Start address
            if (function >= fctWriteMultipleCoils)
            {
                byte[] _cnt = BitConverter.GetBytes((short)IPAddress.HostToNetworkOrder((short)numData));
                data[10] = _cnt[0];			// Number of bytes
                data[11] = _cnt[1];			// Number of bytes
                data[12] = (byte)(numBytes - 2);
            }
            return data;
        }

        // ------------------------------------------------------------------------
        // Create modbus header for write action
        private byte[] CreateReadWriteHeader(ushort id, ushort startReadAddress, ushort numRead, ushort startWriteAddress, ushort numWrite)
        {
            // -----------------------------------------------------------------------------------------------------------
            // byte 0~1 -> Transaction ID (쿼리 및 응답에 관련 한 작업의 순서번호를 나타내며 마스터에 의해 설정됩니다
            // byte 2~3 -> Protocol ID (프로토콜의 ID를 나타내며 0x0000 으로 고정값 입니다
            // byte 4~5 -> 길이 (LENGTH 필드 이후부터 해당 프레임의 마지막까지의 길이를 나타냅니다
            // byte 6 -> Unit ID
            // byte 7 -> FC (모드버스TCP 함수 코드)
            // byte 8 -> Data (함수 코드에 따른 데이터 등)
            // -----------------------------------------------------------------------------------------------------------

            byte[] data = new byte[numWrite * 2 + 17];

            byte[] _id = BitConverter.GetBytes((short)id);
            data[0] = _id[0];						// Slave id high byte
            data[1] = _id[1];						// Slave id low byte+
            byte[] _size = BitConverter.GetBytes((short)IPAddress.HostToNetworkOrder((short)(11 + numWrite * 2)));
            data[4] = _size[0];						// Complete message size in bytes
            data[5] = _size[1];						// Complete message size in bytes
            data[6] = 1;							// Slave address
            data[7] = fctReadWriteMultipleRegister;	// Function code
            byte[] _adr_read = BitConverter.GetBytes((short)IPAddress.HostToNetworkOrder((short)startReadAddress));
            data[8] = _adr_read[0];					// Start read address
            data[9] = _adr_read[1];					// Start read address
            byte[] _cnt_read = BitConverter.GetBytes((short)IPAddress.HostToNetworkOrder((short)numRead));
            data[10] = _cnt_read[0];				// Number of bytes to read
            data[11] = _cnt_read[1];				// Number of bytes to read
            byte[] _adr_write = BitConverter.GetBytes((short)IPAddress.HostToNetworkOrder((short)startWriteAddress));
            data[12] = _adr_write[0];				// Start write address
            data[13] = _adr_write[1];				// Start write address
            byte[] _cnt_write = BitConverter.GetBytes((short)IPAddress.HostToNetworkOrder((short)numWrite));
            data[14] = _cnt_write[0];				// Number of bytes to write
            data[15] = _cnt_write[1];				// Number of bytes to write
            data[16] = (byte)(numWrite * 2);        // Byte count (2 x word count for write)

            return data;
        }

        // ------------------------------------------------------------------------
        // Write asynchronous data
        private bool WriteAsyncData(cWagoCmdMsg iData)
        {
            if ((tcpAsyCl != null) && (tcpAsyCl.Connected))
            {
                try
                {
                    return _Manager.Insert(iData, false);
                }
                catch (Exception)
                {
                    CallException(iData.ID, iData.CmdData[7], excExceptionConnectionLost);
                    return false;
                }
            }
            else CallException(iData.ID, iData.CmdData[7], excExceptionConnectionLost); return false;
        }

        protected internal void SocketWriteData(cWagoCmdMsg iData)
        {
            if ((tcpAsyCl != null) && (tcpAsyCl.Connected))
            {
                try
                {
                    //tcpAsyCl.Send(iData.CmdData, 0, iData.CmdData.Length, SocketFlags.None);
                    tcpAsyCl.BeginSend(iData.CmdData, 0, iData.CmdData.Length, SocketFlags.None, new AsyncCallback(OnSend), tcpAsyCl);
                    //tcpAsyCl.BeginReceive(tcpAsyClBuffer, 0, tcpAsyClBuffer.Length, SocketFlags.None, new AsyncCallback(OnReceive), tcpAsyCl);
                }
                catch (Exception)
                {
                    CallException(iData.ID, iData.CmdData[7], excExceptionConnectionLost);
                }
            }
            else CallException(iData.ID, iData.CmdData[7], excExceptionConnectionLost);
        }

        // ------------------------------------------------------------------------
        // Write asynchronous data acknowledge
        private void OnSend(System.IAsyncResult result)
        {
            if (result.IsCompleted == false) CallException(0xFFFF, 0xFF, excSendFailt);
        }

        // ------------------------------------------------------------------------
        // Write asynchronous data response
        private void OnReceive(System.IAsyncResult result)
        {
            try
            {
                Socket _Sock = (Socket)result.AsyncState;
                int ReadByte = _Sock.EndReceive(result);

                if (result.IsCompleted == false) CallException(0xFF, 0xFF, excExceptionConnectionLost);
                if (ReadByte <= 0) CallException(0xFF, 0xFF, excExceptionConnectionLost);

                // ------------------------------------------------------------
                ushort id = BitConverter.ToUInt16(tcpAsyClBuffer, 0);
                // Byte 7 -> Modbus Function Code
                byte function = tcpAsyClBuffer[7];
                byte[] data;

                // Read / Write response data
                if ((function >= fctWriteSingleCoil) && (function != fctReadWriteMultipleRegister))
                {
                    // Write
                    // Byte 8,9 -> Reference Number
                    // Byte 10 -> Value
                    // Byte 11 ->
                    data = new byte[2];
                    Array.Copy(tcpAsyClBuffer, 10, data, 0, 2);
                }
                else
                {
                    // Read
                    // Byte 8 -> Byte Count
                    // Byte 9 ~ -> Value register 0~
                    data = new byte[tcpAsyClBuffer[8]]; 
                    Array.Copy(tcpAsyClBuffer, 9, data, 0, tcpAsyClBuffer[8]);
                }

                // ------------------------------------------------------------
                // Recive Active
                IAsyncResult ar = tcpAsyCl.BeginReceive(tcpAsyClBuffer, 0, tcpAsyClBuffer.Length, SocketFlags.None, new AsyncCallback(OnReceive), tcpAsyCl);
                //ar.AsyncWaitHandle.WaitOne();

                // ------------------------------------------------------------
                // Response data is slave exception
                if (function > excExceptionOffset)
                {
                    function -= excExceptionOffset;
                    CallException(id, function, tcpAsyClBuffer[8]);
                }
                else
                {
                    // Response data is regular data
                    OnResponseData?.BeginInvoke(id, function, data, null, null);
                    if (_Manager != null) _Manager.ResponseOK();
                }
            }
            catch (Exception)
            {
                CallException(0xFF, 0xFF, excExceptionConnectionLost);
            }
        }

        // ------------------------------------------------------------------------
        // Write data and and wait for response
        private byte[] WriteSyncData(byte[] write_data, ushort id)
        {
            if ((tcpSynCl != null) && (tcpSynCl.Connected))
            {
                try
                {
                    tcpSynCl.Send(write_data, 0, write_data.Length, SocketFlags.None);
                    int result = tcpSynCl.Receive(tcpSynClBuffer, 0, tcpSynClBuffer.Length, SocketFlags.None);

                    byte function = tcpSynClBuffer[7];
                    byte[] data;

                    if (result == 0) CallException(id, write_data[7], excExceptionConnectionLost);

                    // ------------------------------------------------------------
                    // Response data is slave exception
                    if (function > excExceptionOffset)
                    {
                        function -= excExceptionOffset;
                        CallException(id, function, tcpSynClBuffer[8]);
                        return null;
                    }
                    // ------------------------------------------------------------
                    // Write response data
                    else if ((function >= fctWriteSingleCoil) && (function != fctReadWriteMultipleRegister))
                    {
                        data = new byte[2];
                        Array.Copy(tcpSynClBuffer, 10, data, 0, 2);
                    }
                    // ------------------------------------------------------------
                    // Read response data
                    else
                    {
                        data = new byte[tcpSynClBuffer[8]];
                        Array.Copy(tcpSynClBuffer, 9, data, 0, tcpSynClBuffer[8]);
                    }
                    return data;
                }
                catch (Exception)
                {
                    CallException(id, write_data[7], excExceptionConnectionLost);
                }
            }
            else CallException(id, write_data[7], excExceptionConnectionLost);
            return null;
        }
    }
}
