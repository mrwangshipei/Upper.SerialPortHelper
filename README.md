\## SerialPortHelper 串口通信工具类



`SerialPortHelper` 是一个专为自定义串口协议设计的辅助类，适用于需要识别完整帧（如帧头/帧尾/校验）的通信场景，典型用于工业设备、嵌入式系统、仪器通讯等。



---



\### ✅ 核心功能



\* \*\*完整帧判断机制\*\*：通过用户传入的判断逻辑识别是否已收到完整数据帧。

\* \*\*自适应延迟触发机制\*\*：避免数据包截断或早读。

\* \*\*线程安全的读写控制\*\*：多线程环境下可靠通信。

\* \*\*支持缓存查看（Peek）与清除（ReadAndClear）\*\*：灵活管理串口缓冲区。

\* \*\*支持发送后等待响应（SendAndWaitResult）\*\*：封装典型收发流程。



---



\### 🧱 典型用法



```csharp

using System.IO.Ports;

using Upper.SerialPortHelper;



SerialPort sp = new SerialPort("COM3", 9600);

var helper = new SerialPortHelper(sp);



helper.IsFullFrame += (data) =>

{

&nbsp;   // 示例：完整帧以 0xAA 开头，0xFF 结尾，且长度大于等于5

&nbsp;   return data.Length >= 5 \&\& data\[0] == 0xAA \&\& data\[^1] == 0xFF;

};



if (helper.Open())

{

&nbsp;   byte\[] cmd = new byte\[] { 0xAA, 0x01, 0x02, 0x03, 0xFF };

&nbsp;   byte\[] response = helper.SendAndWaitResult(cmd, 100, onlyread: false);



&nbsp;   Console.WriteLine(BitConverter.ToString(response));

}

```



---



\### 📌 方法说明



| 方法                                                                   | 描述               |

| -------------------------------------------------------------------- | ---------------- |

| `SerialPortHelper(SerialPort serial, int MsgsWaitTime = 35)`         | 初始化帮助类，绑定外部串口实例  |

| `bool Open()` / `bool Close()`                                       | 打开 / 关闭串口连接      |

| `void Send(byte\[] msg)`                                              | 发送数据，不等待响应       |

| `byte\[] SendAndWaitResult(byte\[] data, int waittime, bool onlyread)` | 发送数据并等待接收完整帧     |

| `byte\[] ReadAndClear(int waitms)`                                    | 读取数据并清空缓存区       |

| `byte\[] Peek(int waitms)`                                            | 只查看缓存区数据，不清除     |

| `byte\[] Receive(int waitms, bool onlyread = false)`                  | 通用读取函数，支持只读或清除缓存 |



---



\### 🧠 注意事项



\* 本类 \*\*仅支持单个串口实例管理\*\*，如需多串口通信，可自行封装字典管理：`Dictionary<string, SerialPortHelper>`。

\* `IsFullFrame` 是关键逻辑，必须由用户提供完整帧的识别方式，否则延迟机制会退化为定时释放。

\* `SendAndWaitResult` 推荐用于典型“请求-响应”结构的协议封装。

\* 所有手动 `Receive` 的操作，应在确认线程安全的前提下调用。



---



\### ♻️ 资源释放



使用结束后请及时释放资源避免线程挂起或句柄泄漏：



```csharp

helper.Dispose();

```



---



如需英文文档、单元测试示例、或多串口封装样例，可联系补充。



