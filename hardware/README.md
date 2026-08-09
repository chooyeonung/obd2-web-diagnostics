# hardware/ — KiCad 회로도

`obd2-gateway.kicad_sch` — 벤치 회로(모듈 단위)의 KiCad 회로도.
KiCad 7 이상에서 열림 (독립 파일이라 프로젝트 없이 더블클릭으로 열려요).

- 연결은 **같은 이름의 글로벌 라벨**끼리 이어집니다 (+12V_PROT, +5V, +3V3, GND, CAN_H/L, TWAI_TX/RX)
- 심볼은 자체 라이브러리(OBD2DIY)로 파일 안에 내장 — 별도 라이브러리 설치 불필요
- 향후 캐리어 보드 아트웍 시 이 회로도에 풋프린트를 지정하고 PCB로 넘기면 됨

kicad-cli 7.0.11로 파싱/SVG 출력 검증 완료.
