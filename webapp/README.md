# OBD-II 웹 진단 — Blazor WASM 웹앱

vLinker MC+ (ELM327 호환 BLE 동글) 또는 내장 시뮬레이터로 차량 진단을 수행하는 Blazor WebAssembly 앱.

## 실행 방법

```bash
cd webapp
dotnet run
```

브라우저에서 `http://localhost:5000` (콘솔에 표시되는 포트) 접속.

- **시뮬레이터 모드**: 동글/차량 없이 바로 동작. 가상 ECU가 주행 데이터와 고장코드(P0133, P0420)를 흉내낸다.
- **BLE 모드**: Chrome/Edge에서만 동작(Web Bluetooth). vLinker MC+ 전원이 켜진 상태(차량 OBD 포트 연결)에서 "연결" 클릭 → 기기 선택.
  - Web Bluetooth는 **HTTPS 또는 localhost**에서만 동작한다. 외부 배포 시 HTTPS 필수.

## 구조

```
Services/
  IObdTransport.cs      트랜스포트 추상화 (시뮬레이터/BLE/향후 WebSocket 공용 인터페이스)
  SimulatorTransport.cs 가상 ELM327+ECU (실차 없이 전체 흐름 테스트)
  BleTransport.cs       Web Bluetooth JS interop 래퍼
  Elm327Client.cs       ELM327 대화 규칙 (명령 → '>' 프롬프트까지 응답 수집, 초기화 시퀀스)
  ObdService.cs         고수준 진단 API (PID 스냅샷, DTC 조회/소거, VIN) + 응답 파서
  PidDefinitions.cs     SAE J1979 Mode 01 PID 테이블 (여기 추가하면 UI 자동 반영)
  DtcDecoder.cs         DTC 2바이트 → P/C/B/U 코드 + 한글 설명
wwwroot/js/obd-ble.js   Web Bluetooth 모듈 (프레임워크 독립 — 향후 ESP32용 경량 페이지에 재사용)
Pages/Home.razor        대시보드 (연결/게이지/DTC/통신 로그)
```

## 설계 노트

- **트랜스포트 추상화**가 핵심: 자작 ESP32 보드가 완성되면 `WebSocketTransport`를 `IObdTransport`로 추가하기만 하면 나머지 계층은 그대로 동작한다. ESP32 펌웨어가 ELM327 명령을 에뮬레이션하는 것이 전제.
- DTC 소거(Mode 04)는 되돌릴 수 없는 동작이라 2단계 클릭 확인을 거친다.
- 응답 파서는 실차의 흔한 변형(SEARCHING 프리픽스, 멀티라인 응답, ISO-TP 프레임 인덱스 `0:`, NO DATA)에 관대하게 동작하도록 작성했다.

## 다음 단계 (TODO)

- [ ] 실차(vLinker MC+)에서 BLE 연결·PID 폴링 검증
- [ ] Mode 02(프리즈 프레임), Mode 07(미확정 DTC) 추가
- [ ] 시계열 차트(주행 로그) 및 기록 저장
- [ ] PWA 구성(오프라인 동작)
- [ ] 자작 ESP32 보드용 WebSocketTransport 추가
