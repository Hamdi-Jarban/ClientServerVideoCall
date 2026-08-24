# 🎥 VideoCallSystem

«Real-Time Audio & Video Communication System using Client/Server Architecture»

نظام سطح مكتب للتواصل الصوتي والمرئي في الوقت الحقيقي بين المستخدمين، تم تطويره باستخدام C# و.NET 8 وWPF، ويعتمد على بنية Client/Server وتقنيات TCP/IP وUDP لنقل البيانات والتحكم في المكالمات.

---

📌 نبذة عن المشروع

يهدف مشروع VideoCallSystem إلى بناء نظام بسيط وعملي يسمح للمستخدمين بالاتصال ببعضهم عبر الشبكة وإجراء مكالمات صوتية ومرئية في الوقت الحقيقي.

يوفر النظام إمكانية:

- 🔐 تسجيل دخول المستخدم.
- 👥 عرض المستخدمين المتصلين.
- 📞 إرسال طلب مكالمة.
- ✅ قبول أو رفض المكالمة.
- 🎥 إجراء مكالمة فيديو.
- 🎙️ إجراء مكالمة صوتية.
- 📷 تشغيل وإيقاف الكاميرا.
- 🔇 كتم وتشغيل الميكروفون.
- ❌ إنهاء المكالمة.

---

🏗️ System Architecture

يعتمد النظام على Client/Server Architecture:

                    ┌──────────────────────┐
                    │        SERVER        │
                    │                      │
                    │  Connection Manager  │
                    │  User Management     │
                    │  Call Signaling      │
                    └──────────┬───────────┘
                               │
                 ┌─────────────┴─────────────┐
                 │                           │
                 ▼                           ▼
        ┌─────────────────┐         ┌─────────────────┐
        │     CLIENT 1    │         │     CLIENT 2    │
        │                 │         │                 │
        │  WPF Interface  │         │  WPF Interface  │
        │  Camera         │◄───────►│  Camera         │
        │  Microphone     │         │  Microphone     │
        │  Speaker        │         │  Speaker        │
        └─────────────────┘         └─────────────────┘

---

🌐 Communication Architecture

يستخدم المشروع بروتوكولين رئيسيين:

TCP

يستخدم TCP للبيانات التي تحتاج إلى موثوقية وترتيب، مثل:

- تسجيل الدخول.
- إدارة المستخدمين.
- طلبات المكالمات.
- قبول أو رفض المكالمة.
- أوامر التحكم.
- إنهاء المكالمة.

UDP

يستخدم UDP لنقل بيانات الصوت والفيديو في الوقت الحقيقي، حيث يكون تقليل التأخير أكثر أهمية من ضمان وصول كل حزمة.

                    Network Communication
                           │
              ┌────────────┴────────────┐
              │                         │
             TCP                       UDP
              │                         │
      ┌───────┴────────┐        ┌───────┴───────┐
      │                │        │               │
    Control          Signaling Audio          Video
      │                │        │               │
   Login          Call Request  🎙️             🎥
   Users          Accept/Reject
   Commands

---

🎥 Video Pipeline

يتم التقاط الفيديو من الكاميرا ومعالجته باستخدام OpenCvSharp ثم إرساله عبر الشبكة إلى الطرف الآخر.

Camera
   │
   ▼
OpenCvSharp
   │
   ▼
Capture Frame
   │
   ▼
Encode
   │
   ▼
UDP
   │
   ▼
Remote Client
   │
   ▼
Decode
   │
   ▼
Display Video

---

🎙️ Audio Pipeline

يتم التقاط الصوت من الميكروفون باستخدام NAudio ثم إرساله إلى الطرف الآخر.

Microphone
    │
    ▼
  NAudio
    │
    ▼
Audio Capture
    │
    ▼
  Encode
    │
    ▼
   UDP
    │
    ▼
Remote Client
    │
    ▼
  Decode
    │
    ▼
 Speaker

---

🛠️ Technologies

Technology| Purpose
C#| لغة البرمجة الأساسية
.NET 8| منصة التطوير
WPF| بناء واجهة المستخدم
TCP/IP| الاتصال الموثوق والتحكم
UDP| نقل الصوت والفيديو
Socket Programming| الاتصال بين Client وServer
NAudio| معالجة والتقاط الصوت
OpenCvSharp| التقاط ومعالجة الفيديو
JSON| تبادل الرسائل والبيانات
Git / GitHub| إدارة الإصدارات

---

📂 Project Structure

VideoCallSystem/
│
├── VideoCallSystem.sln
│
├── VideoCall.Server/
│   ├── Program.cs
│   ├── Services/
│   ├── Networking/
│   └── ...
│
├── VideoCall.Client/
│   ├── App.xaml
│   ├── MainWindow.xaml
│   ├── Views/
│   ├── ViewModels/
│   ├── Services/
│   └── ...
│
├── VideoCall.Shared/
│   ├── Models/
│   ├── Protocol/
│   └── ...
│
├── README.md
├── .gitignore
└── LICENSE

---

🔄 How It Works

1. Start Server

يبدأ الخادم بالاستماع إلى الاتصالات القادمة من المستخدمين.

2. Connect Client

يقوم تطبيق العميل بالاتصال بالخادم.

3. Login

يقوم المستخدم بتسجيل الدخول إلى النظام.

4. Online Users

يعرض النظام قائمة المستخدمين المتصلين حاليًا.

5. Call Request

يختار المستخدم شخصًا من القائمة ويرسل إليه طلب مكالمة.

6. Accept / Reject

يمكن للمستخدم المستقبل:

- قبول المكالمة.
- رفض المكالمة.

7. Start Call

بعد قبول المكالمة يبدأ الاتصال الصوتي والمرئي.

8. Call Controls

يمكن للمستخدم أثناء المكالمة:

- تشغيل/إيقاف الكاميرا.
- كتم/تشغيل الميكروفون.
- إنهاء المكالمة.

---

📦 Requirements

لتشغيل المشروع تحتاج إلى:

- Windows 10 / 11
- Visual Studio 2022
- .NET 8 SDK
- .NET 8 Desktop Runtime
- Webcam
- Microphone
- اتصال شبكي بين الأجهزة

---

📚 NuGet Packages

المشروع يعتمد على الحزم التالية:

NAudio
OpenCvSharp4
OpenCvSharp4.runtime.win

يمكن تثبيت الحزم باستخدام NuGet Package Manager في Visual Studio.

---

▶️ Running the Project

Server

قم بتشغيل:

VideoCall.Server

سيبدأ الخادم في استقبال اتصالات المستخدمين.

Client

بعد تشغيل الخادم قم بتشغيل:

VideoCall.Client

ثم قم بتسجيل الدخول والاتصال بالخادم.

يمكن تشغيل أكثر من Client لاختبار المكالمات بين المستخدمين.

---

🧪 Testing

يمكن اختبار النظام باستخدام جهازين متصلين بنفس الشبكة المحلية:

┌─────────────────┐
│    Computer 1   │
│                 │
│     Server      │
└────────┬────────┘
         │
       LAN/Wi-Fi
         │
┌────────▼────────┐
│    Computer 2   │
│                 │
│     Client      │
└─────────────────┘

كما يمكن تشغيل Server وClient على نفس الجهاز لأغراض الاختبار والتطوير.

---

🔒 Security

هذا المشروع مخصص للأغراض التعليمية والجامعية.

قبل استخدام النظام في بيئة إنتاجية حقيقية، يجب إضافة طبقات أمنية مثل:

- 🔐 تشفير الاتصالات.
- 🔑 Authentication آمن.
- 🛡️ Authorization.
- 🔒 تشفير بيانات الصوت والفيديو.
- 🧩 التحقق من حزم UDP.
- 🔐 تخزين كلمات المرور بشكل آمن.
- 🛡️ حماية الجلسات.
- 🚫 التحقق من صحة البيانات القادمة من الشبكة.

---

🎓 Educational Objectives

تم تصميم المشروع لدراسة وتطبيق مفاهيم:

- Client/Server Architecture
- TCP/IP
- UDP
- Socket Programming
- Network Programming
- Real-Time Communication
- Audio Processing
- Video Processing
- WPF
- Asynchronous Programming
- JSON Communication
- Network Protocol Design

---

🚀 Future Improvements

يمكن تطوير المشروع مستقبلًا لإضافة:

- 👥 Group Video Calls
- 💬 Text Chat
- 📁 File Transfer
- 🖥️ Screen Sharing
- 📞 Call History
- 👤 User Database
- 🔐 Secure Authentication
- 🌍 Internet-Based Communication
- 🔄 NAT Traversal
- 🎞️ Better Audio/Video Encoding
- 🔒 End-to-End Encryption

---

📊 Project Status

Status: "Educational / University Project"

Version: "1.0.0"

---

👨‍💻 Author

Developed as a university project using:

C# • .NET 8 • WPF • TCP/IP • UDP • NAudio • OpenCvSharp

---

📄 License

This project is intended for educational and academic purposes.
