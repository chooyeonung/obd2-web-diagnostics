# OBD-II WiFi 게이트웨이 펌웨어 (ESP32-S3)

브라우저 ←WiFi/WebSocket→ **[이 펌웨어: ELM327 에뮬레이션]** ←CAN(TWAI)→ 차량 ECU

웹앱의 `WebSocketTransport`와 대화 규격이 맞춰져 있어, 플래시 후 웹앱에서
"자작 보드 (WiFi/WebSocket)" 트랜스포트로 바로 연결된다. Safari/iPhone도 이 경로로 커버.

## 결선 (구매 부품 기준)

| ESP32-S3 (VND018) | 연결 대상 |
|---|---|
| GPIO17 (J3 핀10) | SN65HVD230 **D** (CTX) |
| GPIO18 (J3 핀11) | SN65HVD230 **R** (CRX) |
| 3V3 | SN65HVD230 VCC |
| GND | SN65HVD230 GND, LM2596 OUT- |
| 5V(VIN) | LM2596 OUT+ (**5.0V로 조정 후 연결**) |

- SN65HVD230 CANH → OBD 6번 핀, CANL → OBD 14번 핀
- 전원: OBD 16번(+12V) → 폴리퓨즈 → SS34 → SMAJ24A/P6KE24A(병렬) → LM2596 IN+
- **CJMCU-230 모듈의 120Ω 종단저항(R2) 제거 필수**

## 빌드 & 플래시

[ESP-IDF 5.x](https://docs.espressif.com/projects/esp-idf/en/stable/esp32s3/get-started/) 설치 후 (VS Code ESP-IDF 확장 권장):

```bash
cd firmware
idf.py set-target esp32s3
idf.py build          # web/ 폴더가 LittleFS 이미지로 자동 포함됨
idf.py -p COM포트 flash monitor
```

## 사용

1. 부팅 후 WiFi **`OBD2-DIAG`** (비밀번호 `obd12345`) 접속
2. 브라우저에서 `http://192.168.4.1/` → 내장 테스트 콘솔 (ATZ, 010C 등 즉시 테스트)
3. 본 웹앱에서는 WebSocket 주소 `ws://192.168.4.1/ws` 로 연결

## 벤치 테스트 순서 (차량 연결 전)

1. USB 전원만으로 부팅 → WiFi 접속 → 콘솔에서 `ATZ` 응답 확인 (CAN 없이도 동작)
2. `010C` → CAN 미연결 상태면 `NO DATA` (정상 — 타임아웃 경로 확인)
3. 트랜시버 연결 + CAN 분석기(또는 두 번째 보드)로 루프백 응답 테스트
4. 차량 연결: OBD 커넥터 결선 후 `010C` → `41 0C xx xx` 응답 확인

## 구조

```
main/
  twai_bus.c   TWAI(CAN) 드라이버 — 500kbps, 0x7E8~0x7EF 수신 필터
  obd.c        ISO-TP 최소 구현 (SF 요청, SF/FF+CF 응답 조립, Flow Control)
  elm327.c     ELM327 호환 명령 파서 (AT 설정 + OBD hex 패스스루)
  wifi_ap.c    SoftAP (OBD2-DIAG / 192.168.4.1)
  ws_server.c  WebSocket(/ws) + LittleFS 정적 서빙(웹 콘솔)
web/           LittleFS로 플래시되는 정적 파일 (테스트 콘솔)
```

## TODO

- [ ] BLE(ELM327 에뮬레이션) 듀얼 모드 — 안드로이드/데스크톱 크롬 직결용
- [ ] ADC 전압 감시 (ATRV 실측값 + 저전압 딥슬립)
- [ ] STA 모드 설정 UI (공유기 접속 옵션)
- [ ] OTA 업데이트
- [ ] Blazor 웹앱 publish 산출물을 web/에 넣어 self-hosting (선택)
