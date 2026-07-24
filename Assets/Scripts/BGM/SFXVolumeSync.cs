using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SFXVolumeSync : MonoBehaviour
{
    public Slider volumeSlider;

    void Start()
    {
        // 初始化滑条数值和当前音量同步
        volumeSlider.value = SFXManager.Instance.attackVolume;
        // 滑条变化自动调用音量函数
        volumeSlider.onValueChanged.AddListener(SFXManager.Instance.SetSFXVolume);
    }
}
