# 단계별 개발 프롬프트 가이드
# WPF Video Player Pro — Phase Prompts

---

## 🔰 공통 컨텍스트 (모든 프롬프트 앞에 붙이기)

```
나는 WPF와 ffmpeg.autogen 기반의 비디오 플레이어를 개발 중이야.
- 언어: C# / WPF
- 패턴: MVVM (CommunityToolkit.Mvvm)
- DI: Microsoft.Extensions.DependencyInjection
- FFmpeg 빌드: LGPL
- .NET 버전: 8.0

코드 작성 시 규칙:
1. Code-behind에 로직 금지, ViewModel에 작성
2. 비동기 처리 필수 (async/await)
3. UI 스레드 업데이트는 Dispatcher 사용
4. 인터페이스 기반 설계로 테스트 가능하게
```

---

## 📌 Phase 0 — 프로젝트 기반 세팅

### 프롬프트 0-1: 솔루션 구조 설계
```
위 공통 컨텍스트를 참고해서,
WPF 비디오 플레이어 확장 프로젝트의 솔루션 구조를 설계해줘.

요구사항:
- Core 레이어 (ffmpeg.autogen 래퍼)
- UI 레이어 (WPF, MVVM)
- 모듈 방식 확장 구조 (FFmpegExplorer, MisbParser, AiSubtitle)
- 공통 Shared 레이어 (Models, Services, Helpers)

결과물:
1. 폴더 구조 (트리 형식)
2. 각 프로젝트의 .csproj 내용
3. DI 등록 예시 코드 (App.xaml.cs)
```

### 프롬프트 0-2: 기존 플레이어 코어 래핑
```
ffmpeg.autogen을 사용한 기존 비디오 플레이어 코어가 있어.
이를 모듈이 공통으로 사용할 수 있도록 인터페이스로 추상화해줘.

필요한 인터페이스:
- IVideoPlayer (재생/정지/탐색/속도)
- IVideoDecoder (프레임 디코딩, 스트림 정보)
- IAudioExtractor (오디오 스트림 추출)

각 인터페이스와 기본 구현 클래스 작성해줘.
ffmpeg.autogen unsafe 코드는 별도 래퍼 클래스로 격리해줘.
```

---

## 📌 Phase 1 — FFmpeg 옵션 GUI 빌더 (개발자용)

### 프롬프트 1-1: FFmpeg 커맨드 빌더 UI
```
FFmpeg 옵션을 GUI로 설정하고 커맨드라인을 자동 생성해주는
WPF 모듈을 만들어줘.

기능 요구사항:
- 입력/출력 파일 선택 (FileDialog)
- 주요 옵션 카테고리별 탭 구성
  - 비디오 코덱 (codec, bitrate, fps, resolution, crf)
  - 오디오 코덱 (codec, bitrate, sample rate, channels)
  - 필터 (vf, af — 텍스트 직접 입력)
  - 고급 (자유 텍스트 옵션 입력)
- 하단에 생성된 FFmpeg 커맨드 실시간 미리보기
- 클립보드 복사 버튼
- 실행 버튼 (비동기, 진행률 표시)

MVVM 구조:
- FFmpegCommandBuilderViewModel
- FFmpegCommandBuilderView.xaml
- FFmpegCommandService (IFFmpegCommandService)

코드 전체 작성해줘.
```

### 프롬프트 1-2: 스트림 정보 분석기
```
현재 재생 중인 영상 또는 파일의 스트림 정보를 분석해서
보여주는 패널을 만들어줘.

표시할 정보:
- 컨테이너 포맷, 비트레이트, 재생시간
- 비디오 스트림 (코덱, 해상도, fps, 픽셀 포맷)
- 오디오 스트림 (코덱, 샘플레이트, 채널, 비트레이트)
- 자막 스트림 목록
- 챕터 정보

ffmpeg.autogen으로 AVFormatContext에서 정보 추출하는
StreamInfoService와 WPF TreeView로 표시하는 UI 작성해줘.
```

### 프롬프트 1-3: 인코딩 배치 큐
```
여러 파일을 순서대로 인코딩하는 배치 큐 매니저를 만들어줘.

기능:
- 파일 목록 드래그앤드롭 추가
- 각 파일별 FFmpeg 옵션 개별 설정
- 큐 순서 변경 (위/아래 버튼)
- 전체 진행률 + 현재 파일 진행률 표시
- 완료/실패 상태 표시
- 실패 시 에러 로그 확인

WPF ObservableCollection + BackgroundService 패턴으로 작성해줘.
```

---

## 📌 Phase 2 — MISB 메타데이터 파싱 (개발자용)

### 프롬프트 2-1: KLV 파서 구현
```
MISB ST 0601 표준 기반의 KLV(Key-Length-Value) 메타데이터 파서를 구현해줘.

파싱 대상 태그 (우선순위 높은 것):
- Tag 2: UNIX Timestamp
- Tag 13: Sensor Latitude
- Tag 14: Sensor Longitude
- Tag 15: Sensor Altitude
- Tag 17: Platform Heading Angle
- Tag 18: Platform Pitch Angle
- Tag 19: Platform Roll Angle
- Tag 23: Sensor Relative Azimuth
- Tag 40: Target Width
- Tag 48: Security Local MD Set

구현 내용:
1. KlvParser 클래스 (바이트 스트림 → KlvPacket 목록)
2. MisbSt0601Decoder (KlvPacket → MisbMetadata 모델)
3. ffmpeg.autogen으로 MPEG-TS 스트림에서 KLV 데이터 추출
4. 단위 테스트 (xUnit)

MISB 바이트 파싱 상세 로직도 주석으로 설명해줘.
```

### 프롬프트 2-2: 메타데이터 실시간 시각화 패널
```
MISB 메타데이터를 영상 재생과 동기화해서 실시간으로 보여주는
WPF 패널을 만들어줘.

표시 항목:
- GPS 좌표 (위/경도, 고도)
- 플랫폼 자세 (Heading/Pitch/Roll) — 게이지 UI
- 센서 정보 (상대 방위각, 줌 등)
- 원시 KLV 태그 테이블 (Tag ID | Name | Raw Value | Decoded Value)

타임라인 기능:
- 재생 위치에 따라 메타데이터 자동 업데이트
- 특정 시간대 메타데이터 검색/필터

CSV/JSON으로 전체 메타데이터 내보내기 버튼도 포함해줘.
```

### 프롬프트 2-3: 지도 오버레이 연동
```
MISB GPS 데이터를 WPF 지도 컨트롤과 연동해줘.

사용 라이브러리: Microsoft.Web.WebView2 (Leaflet.js 또는 OpenStreetMap)

기능:
- 영상 재생 중 실시간 위치 마커 이동
- 비행 경로 라인 그리기 (재생된 경로 누적 표시)
- 센서 시야각(FOV) 영역 폴리곤 표시
- 마커 클릭 시 해당 시간대로 영상 탐색
- 경로 데이터 KML/GeoJSON 내보내기

WebView2와 WPF ViewModel 간 JS interop 코드도 포함해줘.
```

---

## 📌 Phase 3 — AI 자막/번역 기능

### 프롬프트 3-1: 오디오 추출 + Whisper 연동
```
영상에서 오디오를 추출하고 OpenAI Whisper API로
자막을 생성하는 서비스를 만들어줘.

구현 내용:
1. AudioExtractionService
   - ffmpeg.autogen으로 영상에서 오디오 추출
   - 16kHz, mono, PCM 변환 (Whisper 최적 포맷)
   - 긴 영상은 청크 단위 분할 처리

2. WhisperTranscriptionService
   - OpenAI Whisper API 호출
   - 응답에서 타임스탬프 포함 세그먼트 파싱
   - 재시도 로직 (API 오류 대응)

3. SubtitleExportService
   - SRT 포맷 생성
   - VTT 포맷 생성
   - ASS 포맷 생성 (스타일 포함)

인터페이스 기반으로 Mock 테스트 가능하게 설계해줘.
```

### 프롬프트 3-2: 실시간 번역 자막 오버레이
```
생성된 자막을 영상 위에 오버레이하고,
실시간으로 번역하는 WPF UI를 만들어줘.

기능:
- 영상 재생 위치와 동기화된 자막 표시
- 원문 자막 + 번역 자막 동시 표시 (2줄)
- 번역 언어 선택 (한국어, 영어, 일본어, 중국어 등)
- 번역 API 선택 (GPT-4o / DeepL)
- 자막 스타일 설정 (폰트, 크기, 색상, 배경 투명도)
- 자막 표시/숨김 토글

번역 캐싱 전략:
- 이미 번역된 세그먼트는 재번역 안 함
- 로컬 SQLite DB에 번역 결과 캐싱

번역 서비스 인터페이스 (ITranslationService) 포함해줘.
```

### 프롬프트 3-3: 자막 편집기
```
생성된 자막을 편집할 수 있는 WPF 자막 편집기를 만들어줘.

기능:
- 자막 세그먼트 목록 (시작시간 | 종료시간 | 원문 | 번역)
- 셀 직접 편집
- 시작/종료 시간 조정 (스피너 또는 타임코드 직접 입력)
- 타임라인 바에서 드래그로 구간 조정
- 세그먼트 추가/삭제/병합/분리
- 영상과 동기화 미리보기
- 편집 후 SRT/VTT로 저장

DataGrid와 CustomControl 조합으로 구현해줘.
```

---

## 📌 Phase 4 — 영상 분석 도구

### 프롬프트 4-1: 장면 전환 감지 + 챕터 생성
```
영상의 장면 전환을 자동으로 감지하고
챕터 마커를 생성하는 기능을 만들어줘.

구현 방법:
- ffmpeg.autogen으로 프레임 단위 디코딩
- 연속 프레임 간 히스토그램 차이 계산
- 임계값 초과 시 장면 전환으로 판단
- 감지된 장면 목록 표시 (썸네일 + 타임코드)
- 챕터 이름 편집 기능
- FFmpeg 메타데이터 형식으로 챕터 내보내기

성능 고려: 백그라운드 스레드에서 처리, 진행률 표시
```

### 프롬프트 4-2: 드론 텔레메트리 오버레이
```
MISB 메타데이터(Phase 2)의 텔레메트리 데이터를
영상 위에 오버레이로 번인하는 기능을 만들어줘.

오버레이 항목 (위치/크기 드래그 가능):
- GPS 좌표
- 고도 (m/ft 전환)
- 속도
- Heading/Pitch/Roll 게이지
- 타임스탬프

기술 스택:
- WriteableBitmap 또는 SkiaSharp으로 프레임에 직접 렌더링
- 오버레이 레이아웃 프리셋 저장/불러오기
- 번인된 영상 FFmpeg으로 재인코딩 내보내기
```

### 프롬프트 4-3: 영상 품질 분석
```
영상 품질을 분석하는 도구를 만들어줘.

분석 항목:
- PSNR (Peak Signal-to-Noise Ratio)
- SSIM (Structural Similarity Index)
- 비트레이트 변화 그래프 (시간대별)
- 프레임별 크기 분포
- 드롭 프레임 감지

시각화:
- LiveChartsCore로 시간대별 품질 지표 그래프
- 품질 저하 구간 하이라이트 표시
- 분석 보고서 PDF/Excel 내보내기

ffmpegav_log 콜백 활용해서 FFmpeg 내부 로그도 파싱해줘.
```

---

## 📌 Phase 5 — 수익화 및 배포

### 프롬프트 5-1: 라이선스 시스템 구현
```
Freemium 모델의 라이선스 시스템을 구현해줘.

무료 기능: 기본 플레이어, FFmpeg 커맨드 빌더, SRT 자막 생성
Pro 기능: MISB 파싱, 실시간 번역, 지도 오버레이, 배치 인코딩

구현 내용:
1. LicenseService
   - 라이선스 키 검증 (오프라인 HMAC 기반)
   - 기기 바인딩 (하드웨어 ID)
   - 라이선스 만료일 확인

2. FeatureFlag 시스템
   - [RequiresPro] 어트리뷰트 또는 IFeatureGate 인터페이스
   - Pro 기능 접근 시 업그레이드 유도 다이얼로그

3. 라이선스 키 생성기 (판매자용 별도 콘솔 앱)

외부 서버 없이 오프라인에서도 동작하게 설계해줘.
```

### 프롬프트 5-2: 자동 업데이트 시스템
```
앱 자동 업데이트 시스템을 구현해줘.

요구사항:
- GitHub Releases에서 최신 버전 확인
- 현재 버전과 비교 (SemVer)
- 업데이트 있으면 변경로그와 함께 알림
- 백그라운드 다운로드 + 진행률 표시
- 다운로드 완료 후 설치 및 앱 재시작

사용 기술:
- GitHub API (Octokit.net)
- Squirrel.Windows 또는 직접 구현
- 다운로드 파일 SHA256 검증
```

### 프롬프트 5-3: GitHub README 및 문서 작성
```
이 프로젝트의 GitHub README.md를 영어로 작성해줘.

포함 내용:
- 프로젝트 소개 (배지 포함: License, .NET version, Release)
- 주요 기능 스크린샷 자리표시자
- 설치 방법 (GitHub Releases에서 다운로드)
- 빠른 시작 가이드
- FFmpeg LGPL 라이선스 고지
- 기여 방법 (Contributing)
- 로드맵 (Roadmap)
- 라이선스 섹션

매력적이고 전문적인 톤으로 작성해줘.
개발자 커뮤니티에 어필할 수 있게 기술적 강점을 부각해줘.
```

---

## 💡 프롬프트 사용 팁

1. **공통 컨텍스트**를 항상 첫 줄에 붙여서 사용하세요
2. 한 번에 하나의 프롬프트만 사용하고, 결과를 검토 후 다음 단계로 넘어가세요
3. 코드가 길 경우 "계속해줘" 또는 "다음 파일 작성해줘"로 이어서 요청하세요
4. 에러 발생 시 에러 메시지 전체를 복사해서 "이 에러 고쳐줘"로 요청하세요
5. 각 Phase 완료 후 Git 커밋 후 다음 Phase로 이동하세요
