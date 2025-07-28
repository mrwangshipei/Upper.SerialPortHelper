using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Upper.SerialPortHelper
{
    /// <summary>
    /// SerialPortHelper 串口通信辅助类，用于实现发送数据后等待接收完整响应的机制。
    /// 建议按帧协议使用（如：Modbus、定长/变长带CRC校验协议等）
    /// </summary>
    public class SerialPortHelper:IDisposable
    {
        /// <summary>
        /// 完整帧校验事件，
        /// 用户可注册一个函数判断数据是否构成完整帧（如CRC校验通过）。
        /// 若返回 true，将终止当前等待，立即返回数据。
        /// </summary>
        public event Func<byte[],bool> IsFullFrame;
        /// <summary>
        /// 日志事件
        /// </summary>
        public event Action<string> Log;
        #region 变量
        private System.Timers.Timer ti;
        private CancellationTokenSource delayTokenSource;
        private readonly object readlock = new object();
        private readonly object writelock = new object(); 
        private readonly object delayLock = new object();
        private readonly object datalock = new object(); 
        private Task currentDelayTask = Task.CompletedTask;
        protected ConcurrentQueue<ManualResetEvent> manualResetEvents = new ConcurrentQueue<ManualResetEvent>();
        protected SerialPort serial;
        protected List<byte> ReceiveData = new List<byte>();
        #endregion
        #region 属性
        public int MsgWaitTime { get; set; }
        public bool isOpen 
        { 
            get 
            {
                return serial?.IsOpen == true;
            }
        }
        #endregion
        #region 构造函数
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="serial">传入外界创建好的SerialPort，不要在外界读，防止出现Close的时候异常</param>
        /// <param name="MsgsWaitTime">完整的一帧数据最少要多久</param>
        /// <exception cref="ArgumentNullException">参数serial不能为空</exception>
        /// <exception cref="ArgumentException">MsgsWaitTime不能小于0</exception>
        public SerialPortHelper(SerialPort serial,int MsgsWaitTime = 35) 
        {
            if (serial == null)
            {
                throw new ArgumentNullException("参数serial不能为空");
            }
            if (MsgsWaitTime < 0)
            {
                throw new ArgumentException("MsgsWaitTime不能小于0");
            }
            this.serial = serial;
            this.MsgWaitTime = MsgsWaitTime;
            ti = new System.Timers.Timer(this.MsgWaitTime);
            ti.Elapsed += Ti_Elapsed;
            serial.DataReceived += Serial_DataReceived;
        }
        #endregion
        #region 私有函数
        private void Ti_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            ManualResetEvent mrs;
            while (manualResetEvents.TryDequeue(out mrs))
            {
                mrs.Set();
                mrs.Dispose();
            }
        }
        /// <summary>
        /// 传入的串口数据到达事件处理逻辑中，
        /// 会自动读取数据并累加到 ReceiveData 中。
        /// 建议外部不要再注册 DataReceived 事件。
        /// </summary>
        private void Serial_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            lock (readlock)
            {
                var num = serial.BytesToRead;
                if (num > 0)
                {
                    var temp = new byte[num];
                    serial.Read(temp, 0, num);
                    lock (datalock)
                        ReceiveData.AddRange(temp);
                    CallAllWait();
                }
            }
        }

        private async Task StartDelayAsync()
        {
            CancellationTokenSource newTokenSource;

            lock (delayLock)
            {
                delayTokenSource?.Cancel(); // 取消旧任务
                delayTokenSource?.Dispose(); // 释放资源

                delayTokenSource = new CancellationTokenSource();
                newTokenSource = delayTokenSource;

                // 把新任务记下来
                currentDelayTask = Task.Run(() => DelayAndReleaseAsync(newTokenSource.Token));
            }

            await currentDelayTask; // 可选，如果你希望等待上一个任务完成
        }

        private async Task DelayAndReleaseAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(MsgWaitTime, token);

                ManualResetEvent mrs;
                while (manualResetEvents.TryDequeue(out mrs))
                {
                    mrs.Set();
                    mrs.Dispose();
                }
            }
            catch (TaskCanceledException)
            {
                // 取消是正常行为
            }
        }
        private void CallAllWait()
        {
            if (IsFullFrame?.Invoke(ReceiveData.ToArray()) == true)
            {
                delayTokenSource?.Cancel(); // 不再等待，立即触发等待完成
                ManualResetEvent mrs;
                while (manualResetEvents.TryDequeue(out mrs))
                {
                    mrs.Set();
                    mrs.Dispose();
                }
            }
            else
            {
                _ = StartDelayAsync(); // 只会保留一个任务
            }
        }

        public void Dispose()
        {
            ti?.Dispose();
            serial?.Dispose();
            delayTokenSource?.Cancel(); // 取消延迟任务
            delayTokenSource?.Dispose();
            while (manualResetEvents.TryDequeue(out var mrs))
            {
                mrs.Set(); // 防止外部线程卡死
                mrs.Dispose();
            }

        }

        #endregion
        #region 暴露给用户的函数
        /// <summary>
        /// 建议这个函数同步执行
        /// </summary>
        /// <param name="data"></param>
        /// <param name="waittime"></param>
        /// <param name="onlyread"></param>
        /// <returns></returns>
        public byte[] SendAndWaitResult(byte[] data,int waittime,bool onlyread)
        {
            Send(data);
            return Receive(waittime,onlyread);
        }
        /// <summary>
        /// 这个函数可以异步
        /// </summary>
        /// <param name="msg"></param>
        public void Send(byte[] msg)
        {
            if (isOpen)
            lock (writelock)
                serial.Write(msg,0,msg.Length);
        }
        /// <summary>
        /// 看数据并且清理
        /// </summary>
        /// <param name="waitms">最多等待多久没有数据就不等了</param>
        /// <returns></returns>
        public byte[] ReadAndClear(int waitms) 
        { 
            return Receive(waitms);
        }
        /// <summary>
        /// 只看数据不清理
        /// </summary>
        /// <param name="waitms">最多等待多久没有数据就不等了</param>
        /// <returns></returns>
        public byte[] Peek(int waitms) 
        {
            return Receive(waitms,true);
        }
        /// <summary>
        /// 这个函数可以异步，但是要注意 onlyread == false 的 Receive 放到最后
        /// </summary>
        /// <param name="waitms"></param>
        /// <param name="onlyread"></param>
        /// <returns></returns>
        public byte[] Receive(int waitms,bool onlyread = false)
        {
            if (ReceiveData.Count > 0)
                lock (datalock)
                    if (ReceiveData.Count > 0)
                        return ReceiveData.ToArray();
            var mst = new ManualResetEvent(false);
            manualResetEvents.Enqueue(mst);
            mst.WaitOne(waitms);
            lock (datalock)
            {
                try
                {
                    return ReceiveData.ToArray();
                }
                finally {
                    if (!onlyread)
                    {
                        ReceiveData.Clear();
                    }
                }
            }
        }
        public bool Open() 
        {
            try
            {
                lock (readlock)
                    lock (writelock) 
                        if(!serial.IsOpen)
                            serial.Open();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        public bool Close() 
        {
            try
            {
                lock (readlock)
                    lock (writelock)
                        if(serial.IsOpen)
                            serial.Close();
                return true;
            }
            catch (Exception)
            {
                return false;
            }

        }
        #endregion
    }
}
