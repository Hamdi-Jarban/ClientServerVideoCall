# عميل VideoCallSystem المحدث

## الوظائف المدعومة

- تسجيل الدخول عبر TCP.
- مكالمة خاصة بين شخصين.
- إنشاء غرفة جماعية والانضمام إليها ومغادرتها.
- بدء وإيقاف وسائط الغرفة من المضيف.
- إرسال صوت وفيديو مرة واحدة إلى خادم SFU.
- استقبال صوت وفيديو جميع أعضاء المحادثة.
- عرض فيديو مستقل لكل مشارك.
- تشغيل وإيقاف الكاميرا وكتم وتشغيل الميكروفون.
- تنظيف موارد TCP وUDP والصوت والكاميرا عند الإغلاق.

## ترتيب الدمج

1. حدّث `VideoCall.Shared` أولًا بالنسخة المتوافقة.
2. حدّث خادم SFU بالعقود نفسها.
3. أنشئ مشروع WPF أو استبدل ملفات `VideoCall.Client` بملفات هذا المجلد.
4. تأكد من أن مرجع `VideoCall.Shared` يشير إلى المشروع نفسه، وليس نسخة قديمة.
5. نفذ `dotnet restore` ثم `dotnet build` على Windows/Visual Studio 2022.
6. شغّل الخادم أولًا.
7. سجّل دخول مستخدمين لاختبار المكالمة الخاصة.
8. سجّل دخول ثلاثة مستخدمين لاختبار الغرفة الجماعية.

## الملفات المهمة

| الملف | الوظيفة |
|---|---|
| `Services/NetworkClient.cs` | TCP والرسائل والأحداث |
| `Services/UdpMediaClient.cs` | إرسال واستقبال UDP إلى SFU |
| `Media/AudioService.cs` | التقاط وتشغيل الصوت |
| `Media/VideoCaptureService.cs` | التقاط الكاميرا وترميز الفيديو |
| `ViewModels/GroupCallViewModel.cs` | إدارة المحادثة الخاصة والجماعية |
| `ViewModels/RemoteParticipantViewModel.cs` | فيديو وحالة كل مشارك بعيد |
| `ViewModels/RoomViewModel.cs` | إنشاء الغرف والانضمام وبدء الوسائط |
| `Views/GroupCallWindow.xaml` | شبكة فيديو المشاركين |

## عقد مهم بين العميل والخادم

يجب أن يرسل العميل الحزمة التالية مع كل صوت أو فيديو:

```csharp
new MediaPacket
{
    SessionToken = network.SessionToken!.Value,
    ConversationId = conversationId,
    SenderUsername = network.Username!,
    MediaType = MediaType.Video,
    SequenceNumber = sequence,
    FragmentIndex = index,
    FragmentCount = count,
    Payload = encodedChunk
};
```

يجب أن يكون `ConversationId` نصيًا، مثل `room1`، وأن يكون مطابقًا للقيمة التي يستخدمها الخادم في `ConversationManager`.

## ملاحظة عن الحسابات

حسابات الاختبار الموجودة في الخادم للتطوير فقط. لا تستخدم كلمات مرور ثابتة في الإنتاج، ولا ترسل كلمات المرور عبر TCP غير مشفر.

## الاختبار

اختبر عزل الغرف بإنشاء غرفتين. يجب ألا يرى مستخدمو الغرفة الأولى أي حزمة من الغرفة الثانية. اختبر أيضًا مغادرة عضو، إيقاف الكاميرا، كتم الميكروفون، إغلاق النافذة، وإغلاق الخادم.

## القيود الحالية

هذه نسخة تعليمية تعتمد على WPF وOpenCvSharp وNAudio. لم يتم تشغيل بناء فعلي داخل بيئة لا تحتوي على .NET SDK، لذلك يجب التأكد من البناء والتشغيل على Windows مع Visual Studio 2022.
