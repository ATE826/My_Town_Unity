using UnityEngine;

public class LightSwitch : MonoBehaviour, IInteractable
{
    [Header("Свет")]
    [SerializeField] private Light[] _lights;        // лампы, которыми управляет выключатель
    [SerializeField] private bool _isOn = false;     // начальное состояние

    [Header("Вентиляторы")]
    [SerializeField] private CeilingFan[] _fans;     // вентиляторы, которые крутятся, пока горит свет

    [Header("Необязательно")]
    [SerializeField] private Renderer[] _lampRenderers; // модели ламп со свечением (Emission)
    [SerializeField] private AudioSource _clickSound;   // звук щелчка

    private void Start()
    {
        Apply();
    }

    // Вызывается игроком по нажатию E
    public void Interact()
    {
        _isOn = !_isOn;
        Apply();

        if (_clickSound != null)
        {
            _clickSound.Play();
        }
    }

    private void Apply()
    {
        foreach (Light lamp in _lights)
        {
            if (lamp != null) lamp.enabled = _isOn;
        }

        foreach (CeilingFan fan in _fans)
        {
            if (fan != null) fan.SetActive(_isOn);
        }

        foreach (Renderer rend in _lampRenderers)
        {
            if (rend == null) continue;

            if (_isOn) rend.material.EnableKeyword("_EMISSION");
            else rend.material.DisableKeyword("_EMISSION");
        }
    }
}