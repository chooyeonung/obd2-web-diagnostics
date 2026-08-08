#pragma once
#include <stddef.h>

// ELM327 호환 명령 처리기.
// 웹앱의 Elm327Client / 시뮬레이터와 동일한 대화 규격을 따른다:
// 입력 = 한 줄 명령, 출력 = 응답 텍스트 + "\r\r>" 프롬프트.
// 반환: out에 쓴 길이
size_t elm327_process(const char *line, char *out, size_t out_cap);

// 설정 초기화 (ATZ와 동일 효과)
void elm327_reset(void);
