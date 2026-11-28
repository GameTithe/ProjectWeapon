# KAN-177 귀환 진행·취소 표시

[Jira KAN-177](https://gametithe.atlassian.net/browse/KAN-177) · [KAN-83 귀환 채널링](https://gametithe.atlassian.net/browse/KAN-83) · [KAN-81 이동·피해 취소](https://gametithe.atlassian.net/browse/KAN-81)

## 연결

1. 기존 던전 Canvas 아래에서 상태를 표시할 RectTransform을 만들고 `ExtractionStatusPanel`을 붙인다. 960×540 기준 가로 336, 세로 68의 영역으로 검증했다. 상태 UI는 포인터 입력을 가로채지 않는다.
2. `Initialize(run, channel, font)`에서 현재 탐험의 `RunState`, 해당 런을 진행하는 `ExtractionChannel`과 기존 한글 TMP 폰트를 넘긴다. TMP Essential Resources는 프로젝트에서 사용한다.
3. 실제 귀환 시작이 수락된 직후에 `Render()`를 호출한다. 매 프레임 `ExtractionChannel.ProcessFrame` 또는 `ZoneRunCountdown.ProcessFrame`으로 이동·피격·귀환 완료를 한 번에 처리한 뒤 `Render()`를 다시 호출한다. 마을 복귀 상태를 반영할 때도 렌더를 갱신한다.

UI는 런의 확정 결과와 채널이 발행한 진행률을 표시한다. 시작은 0%, 1.5초는 50%이며 3초 성공이 확정되면 100%와 완료 문구를 표시한다. 취소 시 바가 사라지고, 다음 시도 또는 런 종료 전까지 취소 사유가 남는다. 성공 보상과 실패 정산은 호스트 흐름에서 처리한다.

이동·승인된 회피와 유효 피해가 제출한 취소 신호의 종류는 런이 해당 신호를 수락할 때만 표시된다. 같은 시각과 우선순위의 취소 신호는 기존 입력 순서대로 처리된다. 마감·사망이 런을 끝내면 진행 UI가 닫힌다.

## 재현

- 귀환진 안에서 귀환을 눌러 0%, 절반, 완료를 확인한다. 완료 시 호스트의 실제 런 결과가 성공이다.
- 다시 시도하며 0.19 입력을 유지하면 진행한다. 0.20 이동 입력이나 승인된 회피를 보내면 바가 사라지고 이동 취소가 표시된다. 다음 귀환을 누르면 0%로 돌아온다.
- 진행 도중 유효한 비치명 피해를 받으면 피격 취소가 표시된다. 피해와 완료가 같은 시각이어도 수락된 취소를 표시한다.
- 제한 시간 만료와 귀환 완료가 같은 프레임이면 완료 UI가 나타나지 않는다. 치명 피해도 런 종료로 전달된다.

Unity Test Runner의 EditMode에서 `ProjectWeapon.ExtractionUI.Tests`를 실행한다. 실제 귀환 접근·채널, 이동 관측, 피해 적용, 런 종료를 연결한 테스트다.
