# KAN-40 등급별 평균 DPS 측정

[KAN-40](https://gametithe.atlassian.net/browse/KAN-40) · [KAN-20](https://gametithe.atlassian.net/browse/KAN-20) · [KAN-54](https://gametithe.atlassian.net/browse/KAN-54) · [KAN-58](https://gametithe.atlassian.net/browse/KAN-58)

Unity에서 `Assets/Scenes/DpsQA/DpsQA.unity`를 열고 Play를 누른다. 씬은 등급 평균, 측정 조건, 누락 등급과 순서 위반을 표시한다. Console의 같은 결과에는 무기 식별자, 원본 및 보정 구분, 사거리, 쿨다운과 타격 수도 나온다. 변경 가능한 검증용 에셋은 같은 폴더의 `DpsCalibration.asset`에 있다.

- 씬의 시전자와 타깃은 1유닛 간격으로 고정돼 있다. 타깃은 각 유효 피해 요청을 적용 ID당 한 번 집계하며, 체력 감소로 도중에 판정이 종료되지 않는다.
- 50ms 가상 시계로 0ms를 포함하고 12,000ms를 제외하는 창을 세 번 실행한다. 화면 프레임률, 에디터 일시 정지와 기기별 시간차는 가상 측정에 영향을 주지 않는다.
- 동일 표적과 무기에 기본 공격만 적용한 결과와 공통 검증용 수동 스킬을 포함한 결과를 별도로 계산한다. 시드 40에서 각 회차의 첫 스킬 입력은 350·100·50ms이고, 이후 같은 회차에는 2,000ms마다 입력한다. 모든 무기가 같은 스킬 에셋과 입력 시점을 사용한다. 스킬 피해 8은 검증 가안이다.
- 무기별 DPS는 누적 피해를 총 측정 시간으로 나눈 값이다. 등급 평균에서는 각 무기 사례에 같은 가중치를 준다. 인접한 측정 등급에서 상위 평균이 하위 평균 이하면 위반이다. 누락 등급이 있을 때도 통과를 표시하지 않는다.

## 검증용 결과

| 등급 | 사례 수 | 기본 평균 DPS | 공통 스킬 포함 DPS |
| --- | ---: | ---: | ---: |
| 1 | 3 | 10.28 | 14.28 |
| 2 | 1 | 15.17 | 19.17 |
| 3 | 1 | 20.00 | 24.00 |
| 4 | 1 | 23.75 | 27.75 |
| 5 | 1 | 32.00 | 36.00 |

1등급의 낡은 검·나무 창·철퇴는 실제 일반공격 에셋을 참조한다. 사냥 활과 단검의 공격 에셋은 착수한 main에서 실행 가능하지 않았고, 2~5등급에는 등급별 원형 피해 패턴의 보정 사례를 사용했다. 각 무기 전용 스킬의 품질, 전체 제품 무기의 성능과 균형은 이 수치로 판정할 수 없다.

## 자동 재현

```powershell
unity open . --batchmode --until-ready
unity command test run --project-path . --mode playmode --async-tests true --filter DpsQaMeasurementsTests
unity command test status --project-path .
```

실제 QA 씬 출력, 동일 시드 재현, 다른 시드 오프셋, 여러 타격과 공통 스킬 피해, 약하거나 같은 상위 등급 탐지, 누락 등급과 잘못된 시계 설정을 검증한다.
