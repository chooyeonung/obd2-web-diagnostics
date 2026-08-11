// ============================================================
// OBD_CAN_ESP32_V2 케이스 (파라메트릭 초안)
// 좌표계: 보드 좌하단 = (0,0). X0=안테나(왼쪽), X64=J1/USB(오른쪽)
// ============================================================

/* ----- 실측 후 조정할 파라미터 (TBD) ----- */
H_BOT   = 8.5;   // PCB 아랫면 여유 (J1 하우징 ~7mm + 여유) [실측 확인]
H_TOP   = 16;    // PCB 윗면 여유 (소켓+데브킷+USB 상단) [실측 확인]
USB_Z0  = 9.5;   // PCB 윗면 기준 USB 개구 시작 높이 [실측 확인]
USB_Z1  = 15.5;  // USB 개구 끝 높이 [실측 확인]

/* ----- 보드 데이터 (PCB에서 추출한 고정값) ----- */
BW = 64;  BH = 36;  PCB_T = 1.6;
holes = [[3,1.6],[61,1.6],[3,34.4],[61,34.4]];   // M2.5 (2.7mm NPTH)
// J1 (XH 6P, 뒷면, +X로 케이블): kicad y99~111.5 → local Y 12.5~25
J1_Y0 = 11;  J1_Y1 = 26;
// 데브킷: Y 4.7~30.1 폭, USB는 +X 방향 오른쪽 끝
USB_Y0 = 7;  USB_Y1 = 29;
// 안테나: X0 왼쪽으로 ~3mm 돌출
ANT_GAP = 5;     // 왼쪽 내부 여유

/* ----- 케이스 파라미터 ----- */
WALL = 2.2;      // 벽 두께
GAP  = 1.0;      // 보드 주변 여유 (좌측 제외)
R    = 3;        // 외곽 라운드
FLOOR = 2.2;  ROOF = 2.2;
BOSS_D = 6.5;  BOSS_HOLE = 2.15;   // M2.5 셀프태핑
PILLAR_HOLE = 2.8;                  // 리드 관통(스크류 통과)
CBORE_D = 5.6;  CBORE_H = 2.5;      // 접시머리 카운터보어

/* ----- 파생 치수 ----- */
IX0 = -ANT_GAP;          IX1 = BW + GAP;     // 내부 X범위
IY0 = -GAP;              IY1 = BH + GAP;
OX0 = IX0-WALL; OX1 = IX1+WALL; OY0 = IY0-WALL; OY1 = IY1+WALL;
PCB_Z = FLOOR + H_BOT;                        // PCB 아랫면 z
TOP_Z = PCB_Z + PCB_T + H_TOP;                // 내부 상단 z
LID_SPLIT = PCB_Z + PCB_T;                    // 분리면 = PCB 윗면

$fn = 40;

module rbox(x0,y0,x1,y1,h,r) {  // 라운드 사각 기둥
  hull() for (p=[[x0+r,y0+r],[x1-r,y0+r],[x0+r,y1-r],[x1-r,y1-r]])
    translate([p[0],p[1],0]) cylinder(h=h, r=r);
}

/* ============ 베이스 (하부) ============ */
module base() {
  difference() {
    rbox(OX0,OY0,OX1,OY1, FLOOR+H_BOT+PCB_T, R);
    // 내부 공동
    translate([0,0,FLOOR]) rbox(IX0,IY0,IX1,IY1, H_BOT+PCB_T+1, R-WALL/2);
    // J1 케이블 개구 (PCB 아래, 오른쪽 벽)
    translate([IX1-0.1, J1_Y0, FLOOR+1])
      cube([WALL+2, J1_Y1-J1_Y0, H_BOT+PCB_T]);
    // 바닥 통풍 슬롯
    for (i=[0:3]) translate([14+i*10, 6, -1]) cube([3, BH-12, FLOOR+2]);
  }
  // 나사 보스 (PCB 지지)
  for (h=holes) translate([h[0],h[1],FLOOR])
    difference() {
      cylinder(d=BOSS_D, h=H_BOT);
      translate([0,0,-1]) cylinder(d=BOSS_HOLE, h=H_BOT+2);
    }
  // J1 하우징 받침 턱 (커넥터 몸통 아래 지지)
  translate([BW-2, J1_Y0+1.5, FLOOR]) cube([GAP+WALL+2-0.4, J1_Y1-J1_Y0-3, H_BOT-7.2]);
}

/* ============ 리드 (상부) ============ */
module lid() {
  difference() {
    union() {
      rbox(OX0,OY0,OX1,OY1, ROOF + H_TOP, R);
    }
    // 내부 공동
    translate([0,0,-1]) rbox(IX0,IY0,IX1,IY1, H_TOP+1, R-WALL/2);
    // USB 개구 (오른쪽 벽, PCB 위)
    translate([IX1-0.1, USB_Y0, USB_Z0])
      cube([WALL+2, USB_Y1-USB_Y0, USB_Z1-USB_Z0]);
    // 상단 통풍 슬롯 (벅 컨버터 상부: X 20~45)
    for (i=[0:4]) translate([22+i*5, 8, H_TOP-1]) cube([2.5, BH-16, ROOF+2]);
    // 나사 관통 + 카운터보어
    for (h=holes) translate([h[0],h[1],0]) {
      translate([0,0,-1]) cylinder(d=PILLAR_HOLE, h=H_TOP+ROOF+2);
      translate([0,0,H_TOP+ROOF-CBORE_H]) cylinder(d=CBORE_D, h=CBORE_H+1);
    }
  }
  // 나사 기둥 (리드→PCB까지 내려옴)
  for (h=holes) translate([h[0],h[1],0])
    difference() {
      cylinder(d=BOSS_D, h=H_TOP);
      translate([0,0,-1]) cylinder(d=PILLAR_HOLE, h=H_TOP+2);
    }
}

/* ============ 출력 선택 ============ */
part = "assembly";   // base | lid | assembly | exploded

if (part=="base") base();
if (part=="lid") lid();
if (part=="assembly") {
  base();
  translate([0,0,LID_SPLIT]) lid();
  // PCB 시각화 (초록)
  %translate([0,0,PCB_Z]) cube([BW,BH,PCB_T]);
}
if (part=="exploded") {
  base();
  translate([0,0,LID_SPLIT+28]) lid();
  %translate([0,0,PCB_Z+12]) cube([BW,BH,PCB_T]);
}
