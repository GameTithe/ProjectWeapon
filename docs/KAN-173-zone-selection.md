# KAN-173 로비 구역 선택

실제 SortieLoop 로비에서 **구역 선택**을 열면 검증판 5구역의 번호·이름, 권장 등급, 보상 등급 범위, 제한 시간, 입장 상태가 표시된다. 행은 프로젝트의 `PrototypeZoneCatalog` 설정을 읽고 번호 순으로 정렬한다. 권장 등급은 장비 준비용 표기다. [KAN-173](https://gametithe.atlassian.net/browse/KAN-173) · [KAN-88](https://gametithe.atlassian.net/browse/KAN-88)

| 입장 상태 | 동작 |
| --- | --- |
| 해금 · 선택 | 폐성 1구역은 선택하면 기존 장비 화면을 연다. 확인하면 기존 방식으로 장비 실물을 예약하고 ZoneOneGreybox를 로드한다. |
| 잠김 | 저장본이 기록한 해금 범위 밖이다. 구역 정보는 볼 수 있고 입장 버튼은 숨긴다. |
| 해금 · 입장 준비 중 | 해금은 확인했으나 이 로비에서 입장 경로가 준비되지 않은 구역이다. 현재 2~5구역은 이 상태 또는 잠김으로 표시된다. |

구역 화면을 닫으면 로비로 돌아간다. 구역을 골라 들어간 장비 화면을 취소하면 구역 화면으로 돌아간다. 로비의 기존 **출격**은 폐성 장비 화면의 바로가기다. 구역을 둘러보거나 장비 화면을 열 때는 새 예약을 저장하지 않는다. 확인 시 기존 저장 개정 검증을 거친다. 복구 보류 상태에서는 구역 선택과 출격을 열 수 없다. 보스 처치 후 성공 귀환의 확정 영수증에서 해금 상태를 복원한다. [KAN-86](https://gametithe.atlassian.net/browse/KAN-86) · [KAN-193](https://gametithe.atlassian.net/browse/KAN-193)

## 재현

1. Unity 6000.6.9f1에서 `Assets/Scenes/SortieLoop/SortieLoop.unity`를 열고 Play를 시작한다.
2. 로비의 **구역 선택**을 눌러 다섯 행과 구역별 수치를 확인한다. 새 저장에서는 1구역만 선택할 수 있다.
3. 1구역을 선택한 뒤 장비를 선택하고 확인한다. 폐성의 실제 이동과 전투 화면이 열린다. 장비 화면의 취소도 확인한다.
4. 폐성 보스를 처치하고 귀환을 성공 확정한 저장을 다시 연다. 2구역은 **해금 · 입장 준비 중**, 3구역은 **잠김**으로 보인다.

~~~powershell
unity test . --mode PlayMode --filter ProjectWeapon.SortieLoop.Tests.SortieLoopTests --output "$env:TEMP/KAN-173-sortie-playmode.xml" --timeout 360
~~~

PlayMode는 실제 로비 Button의 Canvas raycast, 다섯 행의 설정값과 글리프, 낮은 등급 무기의 정상 출격, 잠긴 구역의 입장 버튼 없음, 실제 귀환 영수증을 다시 연 구역 해금과 메뉴 조회 전후의 저장 바이트 일치를 확인한다. 낮은 등급 사례는 테스트에서만 1구역 권장 등급을 2로 올린 복제본을 넣어 검증한다.

전용 512×512 정적 SDF 폰트는 기존 프로젝트와 같은 Noto Sans CJK KR Regular의 고정 리비전에서 필요한 문자만 구웠다. 원본 폰트는 런타임에 포함되지 않는다. 사용한 리비전, 해시, SIL OFL 고지는 `Assets/ProjectWeapon/SortieLoop/ZoneSelectionHangul-OFL.txt`에 있다.
